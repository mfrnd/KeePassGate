# The store

<!-- The name the papers on the spike branch give this design, quoted where a reader first meets
     it and nowhere else. -->
<!-- cspell:ignore crdt -->
<!-- KeePassXC's command line and a report on the spike branch, each named as it is spelled
     there. -->
<!-- cspell:ignore keepassxc keepassdx -->

Reference for where KeePassGate keeps what it knows about a database, and how that is written and
read. [`CA.md`](CA.md) covers the authority whose document the store holds, [`DESIGN.md`](DESIGN.md)
the writer and the rules, [`PAIRING.md`](PAIRING.md) the flow that writes an admission, and
`spikes/storage-survival` holds every measurement quoted here.

**The one sentence.** Everything the plugin keeps about a database is an ordinary entry in a group
of the plugin's own, written once, under a check keyed by a secret the database holds, and whatever
takes access away is a further entry, never an entry deleted or changed.

> **Decided, and built in part.** This document is the contract for the store of release 0.3.0.
> Release 0.2.0 ships a store in the database's own custom data, with a `KPG` key in the custom data
> of groups and entries, and what of it this build still keeps is in `CA.md` §204. What is built of
> this store: the API's guard of §633, with what tells a node of the store by its identifier; the
> reading of §620 to §624, with what is in force under the store key, the room a store has for its
> next write (§627, §643), and the words `split`, `outdated` and `stopped` of §656; the
> authority and its retirements of §614, written by the plugin's own writes of §627, with the
> records set back of §628 and what a write asks of KeePass's windows (§626), and served from what
> stands, with `lost` (§656) answered from what the gate last served with; the admissions and
> the revocations of §615, with a single certificate taken away (§648), by its own client or by
> the operator, and the rule about renewals made from it: read by their meaning at every hold,
> written by the writes of a pairing, a renewal and a certificate given up, each saved before it
> is answered, and by the clients page at the settings dialog's OK, where the X revokes a client
> and a row's menu takes a single certificate away (§636); and the profile names of §616, each
> name added by a record and taken off by a record: read by their meaning with a client's
> admission in the hold that reads it, added by a pairing in the admission's own write, and
> added and removed on the Profiles page at the settings dialog's OK, which leaves out an Add
> that a removal of its name overtook (§627); and the rules of §617, a version of a profile's
> row and of a node's switch as records, with nothing of a rule on a group or an entry: read by
> the walk of §617 from the reading of the hold a rule is asked in, and written through the
> writer as the dialog of a group or an entry closes with OK, from a grant editor that holds
> every press until then, with the write above, by which a change at a node writes anew the
> versions below it that were following (§617, §629, §657). §614, §615, §616, §617, §620,
> §626, §627, §628, §629, §633, §636, §643, §648, §656 and §657 state which parts those are,
> and nothing else below describes what the plugin does today. And the standing answers to a
> renewal of §618, a version of `renew:` and the client key: read with the admission in the
> hold a renewal is answered in, written by the renewal dialog's Refuse menu in a turn of the
> writer and saved, and lifted by **Allow renewal** at the settings dialog's OK. Nothing of the
> plugin's is in the custom data. **Not built**: the
> page that seals a store again (§625) and the command that starts it again (§632); the guard
> over KeePass's own windows, with its switch (§635); the tab's listing of the store (§636);
> the raising of the format, with `frozen` (§647, §656); and the dialog that keeps a store
> among several (§649). §644 lists what is not measured, with what each pull request ran of it.
> Each pull request that builds a part of this store brings `CA.md`, `DESIGN.md`, `PAIRING.md`,
> `API.md` and `docs/` into line with the code it adds. Until then those documents hold for
> 0.2.0 where they describe a part not built, and this document holds for what replaces its
> store.

**How to read it.**

- **A sentence with no mark states a decision of the project**, taken between 2026-11-15 and
  2026-11-17 and listed in section 2 of `EVALUATION.md` on `spikes/storage-survival`. "Evaluation
  4.3" is section 4.3 of that file, and "decision 18" is row 18 of the table in its section 2. A
  decision taken on 2026-11-17 after that table was written is not in it, and is cited as **a
  later decision** where it applies: on a second store key (§649), on a single certificate taken
  away (§648), on the guard's switch (§635), on the plugin's version and the store's format
  (§647), on the move from 0.2 (§645, §654), and on what §646 had put to the project: the words
  a client is answered with (§656), a record stripped of its strings (§621), a store started
  again (§632), where a rule waits (§629), the grant editor (§657), what an expired certificate
  left (§643), and the recycle bin (§619). Two further decisions followed: the words of §656
  stand, and a rule is stored as a record for each node and profile (§617). Two more answered
  what §646 then put to the project: what removing a row writes under a switch that refuses
  (§617), and what the gate does with a store that has no room for its next write (§643).
  Later decisions again answered what the building of the rules put to the project: no
  ceiling on the profiles ruled at a node (§617), a row of a rule in several versions that
  offers its X and no list of rules (§657), what the check box shows where the switch stands in
  versions that differ (§657), and what the operator is shown where a node dialog's OK could
  not write (§629). And later decisions settled what was left of rules changed apart: a version
  that lets a profile through records what arrived, and the reading cuts to it (§617, §639);
  the X takes a rule off also under a switch that refuses, which reverses the decision on a
  removed row named above (§617); and a version that no longer fits a string is left without
  its renewal while the rest of the write is written (§627). And with the building of the
  rules: a mask a version records is what the tab showed arriving at the press, cut to what
  arrives at the node in the write's own reading (§617); a state has a column of its own in
  the grant editor's table, with a tooltip for each word; and every text above that table
  takes at most two lines (§657).
- **Measured** names a fact that was run, with where: a report is named by its file name under
  `papers/ground/` on that branch, without `.md`, and its section, as `measured-decided-store` 3.5.
  The reports hold the counts and the reproduction of each run; this document quotes a count only
  where a decision rests on it.
- **The measured code** is the probe family `spikes/storage/probes/store-as-a-whole` on that
  branch, in which the store was composed and run on real files: `probe/store.py` writes the
  records, `probe/read.py` reads them, `probe/Store.cs` makes the entries through KeePassLib 2.62,
  and `model/crdt_model.py` holds the order of steps of a write. A format given here "as measured"
  is the format that code wrote and read. **That makes it what was run, and not what was
  decided.** A measured format is a decision only where a decision names it, as a revocation by
  the client's key is. Everywhere else it is the format proposed for the product, because every
  count quoted is a count of it: a pull request that builds another form states the difference
  and what covers the part it changed. Where the probe's form is a stand-in or an artifact of the
  probe, the sentence names it as that, and §605 lists those places.
- **A rule as a record for each node and profile was decided before it was run, and has been
  run since** (`measured-rules-per-profile`). Every run before that wrote a version of a node's
  whole rule. What this document states of rules is measured in the decided form where the
  sentence names that report, and in the whole-rule form where the sentence names that form.
  §644 has what the run showed, what the build of the rules ran of it and what stays owed.
- **Proposed** marks a design this document makes where a decision names a result and leaves the
  form open, or asks for a design. It is reasoned and not run, and it stands until a decision or a
  measurement changes it. **This document's own** marks a rule that tightens the measured
  code where that code would end wider or would lift a stop, with the reason; it is not run
  either.
- **The design paper** is `papers/options/crdt/design.md` on that branch. The papers call the
  design `crdt`, for a conflict-free replicated data type: data shaped so that copies changed
  apart can be brought together in any order with the same result. A statement marked "the design
  paper" was written there and not run.
- **Reasoning** marks what follows from measured facts and was not itself run. **Not measured**
  marks what nobody ran.
- **To settle** marks what neither a decision, nor the measured code, nor the design paper fixes.
  Each such point is an item of §646, cited as S and its number. Nothing in §646 is decided, and
  an option named there is a proposal.
- **What §646 left to the pull request that builds the store's core is fixed, and is no decision
  of the project.** Each is a name, a length, a ceiling or an order that an implementer would
  otherwise have had to choose. It follows from the conditions its item stated, from what is
  decided and from what was measured, and the measured form is kept wherever nothing speaks
  against it. A sentence that states such a detail names its item, as "S12", and §646 lists
  where each is. None of them was run. Each names what holds it: a test, by the fixture that the
  pull request building that part writes.
- **What §646 left to the pull requests that build the store's first writes is fixed in the
  same manner**: the content of an authority, of an admission and of a revocation, a certificate
  taken away, the texts a person reads, and what keeping an authority writes. A sentence that
  states such a detail names its item, and §646 lists where each is. None of them was run.
- **What §646 left to the pull request that builds the rules is fixed in the same manner**: the
  form of a rule's version and of the switch's, what a deletion record tells of each, the limit
  the resolver carries, and what a node dialog's OK writes. A sentence that states such a detail
  names its item, and §646 lists where each is. None of them was run but where the sentence
  names a report. The reports of the runs made for the rules since are named as every report
  is: `measured-rules-owed-forms`, `measured-rules-cut-parts` and
  `measured-rules-across-levels`, each made in the same probe family, twice with the same
  output.
- **Wider** is a client reaching more than the last decision of the operator allowed. A sequence
  that ends wider is **fail-open** and is named as that. **Refusing** is the plugin refusing what
  the last decision allowed. **Narrower** is a database granting less than the last decision
  without refusing as a whole.
- **A gate** is a KeePass that runs the plugin and serves a database. A gate answers from the
  database KeePass holds in memory, which is **the gate's copy**. **A stale copy** is a copy of the
  database that has not received a decision made in another copy. **A copy of a record** is an
  entry that holds a record's strings under an identifier other than the identifier the record's
  content gives.
- **Create, Recreate and Delete** are the KPG tab's commands for the authority. **Create new IDs**
  is something else: the method KeePass's File, Import preselects, which gives every arriving
  entry a new identifier. **A quick edit** is a change made from KeePass's main window without the
  entry dialog, such as setting an entry to expire now.

---

## 1. What the store is, and why it is entries §601

The store is a group of the plugin's own in the database, visible and named `KeePassGate`, holding
an ordinary entry for each thing the plugin has written down: an authority, an admission, a
revocation, a profile name added or removed, a rule, an answer to a renewal. The plugin writes an
entry once and never changes what it wrote. A decision that takes something away is a further
entry. Each entry carries a check, a keyed digest of its own content under a secret kept in the
database, the **store key**, so that an entry edited by hand or planted from outside does not
pass. With every write the plugin also writes a **summary entry**, which restates everything in
force that takes access away, so that such a decision has a second carrier. The papers call the
summary entry a head.

Nothing is kept in custom data, nothing of the plugin's sits on the operator's own entries or
groups, no time decides anything, and nothing about a database is kept outside its file.

### The requirement, and when a program is compatible §602

**The requirement.** A KeePassGate database is a single file that several programs write. The
store has to come through that file being saved, edited, synchronized and merged by every program
listed as compatible.

**A program is compatible when measurement shows all three:**

1. its save keeps every place in the file the store uses;
2. it merges, or asks, before writing over a file that changed underneath it;
3. its merge keeps what KeePass's own Synchronize keeps, in every place the store relies on.

Which programs are compatible is a measured property, held against these criteria, and never a
preference. A program that loses data only when a person gives a wrong answer to its question may
be listed, with that answer stated as a condition. Survival is pass or fail: a place in the file
that a compatible program loses in a save, an edit, a synchronization or a merge cannot be the
only carrier of anything the plugin keeps (decision 1). §637 has the programs.

### What was measured of today's store §603

Today's store is the database's own custom data, with a node's rule in that node's custom data,
`CA.md` §204. What reopened it was measured on `spikes/revocation-merge` and then on
`spikes/storage-survival`:

- **A KeePassXC merge replaces the custom data of the database it merges into with the other
  copy's, whole**, whenever the other copy was read in a later second, `CA.md` §204. With the
  authority in custom data, a KeePassXC merge from a stale copy that lacks it and holds another
  key removed the authority in 8 of 8 merges (evaluation 4.2).
- **A group's custom data is never merged by KeePassXC**, so a narrowing made in the other copy is
  lost (evaluation 3; `CA.md` §204).
- **A save in KeePassDroid 2.7 deletes the custom data of every group and entry**, and a save in
  KeePassDX 3.2.0 or 3.3.0 that of every group, `CA.md` §204.
- **A merge takes an entry's strings and attachments from a single version, whole**, picked by a
  modification time in whole seconds, and at equal times the other version is usually kept
  nowhere. So nothing the plugin keeps may be changed in place, and no time may decide (evaluation
  3).
- **An entry written once and never changed was kept by every save that was run, in every program,
  and arrived in every merge that was run for it.** What removes it is a deletion record that a
  merge applies, a program's own delete, and Delete Duplicate Entries when a copy under another
  identifier exists (evaluation 3).
- **Deletion records travel unevenly.** KeePass carries a deletion record only under Synchronize,
  KeePassXC applies and copies none, and KeePass's Database Maintenance removes every record at
  once with no question. So a deletion cannot carry a decision (evaluation 3).
- **No other place in the file does better than entries.** Of the 117 places of the format that
  were listed, 113 of them measured, none is united by every merge that was run
  (`measured-kdbx-places` 1, 7.1).
- **A field a program does not know can make it refuse the file**, so the store uses nothing but
  what every program writes itself (evaluation 3).

No store inside the file survives everything, this store included: §638 has every sequence that
still ends wider or refusing.

### What this reverses §604

Each of these is a settled decision of the product, recorded in `CLAUDE.md`, `CA.md` and
`DESIGN.md`, and each is reversed on the measurements of `spikes/storage-survival`. `EVALUATION.md`
there is the argument, and `RESULTS.md` there records every measurement with its reproduction.

| Was, and is what 0.2.0 does | Becomes, in 0.3.0 | Why |
| --- | --- | --- |
| The database's own `CustomData` is the store | entries in a group of the plugin's own, and no custom data anywhere | a KeePassXC merge replaces the whole dictionary, §603 |
| The authority takes the file to KDBX 4.1, for the time each custom data key carries | KDBX 4.0, nothing above it: measured for an authority and an admission in the store, while what a build still keeps in custom data takes the file to 4.1, §612 | no time decides anything, and nothing the store uses needs 4.1 |
| A rule lives in the custom data of its group or entry, and the grant editor writes into the dialog's own copy | a rule is a record in the store for each node and profile, and nothing is on the node | KeePassXC never merges a group's custom data, and two programs delete it at a save, §603 |
| The profile list is a single key, replayed as a difference | an entry for each addition and each removal | a single value is taken whole from whichever copy a merge prefers |
| A revocation names a certificate and is pruned once that certificate has expired | a revocation names the client's key and is never pruned | a renewal at a gate not yet told escaped a revocation by certificate, §615 |
| A revocation, a retirement and an authority are keys only ever added, which a KeePass merge never removes | entries written once, which every merge that was run brings together | the same argument, in the carrier that also comes through KeePassXC and the Android programs in scope |
| Nothing in the store is authenticated beyond the file | every record carries a check under the store key | a foreign file merged in could otherwise add a rule, a record or a retirement, §608 |
| A record that has expired is removed by the plugin's next write | what a record replaces is deleted at the plugin's next write, and nothing is removed for its age | no time decides anything, and nothing in the file tells which copies have seen a record, §643. What an expired certificate left stays until the compaction, by a later decision |
| A database holds the keys it holds, and a file merged in can only add an authority of its own | records under more than a single store key stop the gate until the operator keeps a store | a key that vouches for itself is not thereby the database's own, §649 |
| A client's own revocation and the operator's write the same key | the operator's revocation names the client's key, and a single certificate, given up by its client or taken away by the operator, gets a record of its own | a revocation by key would refuse the certificate a renewal just issued, §648 |

**What is not reversed.** Every database is still its own certificate authority, stored inside
itself, and a malicious `.kdbx` still vouches only for itself. The admission record is still load
bearing. Nothing about a database is kept on the machine a gate runs on (decision 2). Claims
still carry profile names only. The rule model is today's (§617). The single writer, its save and
its reading back are today's (§626). Everything on the KPG settings tab still waits for that
dialog's OK.

---

## 2. The parts §605

Every format in this section is given as measured, from `probe/store.py`, `probe/read.py` and
`probe/Store.cs`, unless the sentence names another source.

**Where the probe's form is a stand-in or an artifact**, and so no proposal for the product: the
member `v` and its value, which the product does not write (S1, §607); the nonces and the store
keys, derived from fixed texts (S2); the
authority's content, which held no certificate (S3); the admission's members (S4); a revocation
written for each certificate (S5); the answer to a renewal, in its object and its texts (S7); the
rule documents, and a rule as a version of a node's whole rule, where a later decision has a
record for each node and profile, which `probe/rows.py` wrote in a later run (§617, S8); a
title made of the kind and a piece of the identifier (S14); and the
reading of several store keys, which holds every key found and counts under each, where §649
departs from it. The kinds of record that this document adds were in no run at all: a
certificate taken away (§648), a keep record (§649) and the version record (§650). §658 fixes
of each what the core reads, and leaves the rest to the pull request that brings its meaning.

### The group §606

A single group of the plugin's own, directly under the root, made with the first authority (the
design paper 3.1).

| | As measured |
| --- | --- |
| Identifier | 16 bytes: the eight bytes `4B 50 47 2F 53 54 4F 52`, the ASCII of `KPG/STOR`, then eight random bytes drawn when the group is made |
| Name | `KeePassGate` |
| Icon | KeePass's icon 29, `TerminalEncrypted`, the plugin's own |
| Notes | `KeePassGate data.` |
| Searching and auto-type | both off |

**It is found by the prefix of its identifier**, among the groups of the whole tree, wherever it
is. Nothing reads its name, its notes, its icon or its place. With searching and auto-type off,
KeePass's quick search and global auto-type never reached its entries, while Find All and the Last
Modified list show them (`measured-desktop-windows` 7; `measured-hide-and-lock` 2.3).

**The random half is why a group made again is another group.** A group made after the earlier
group was deleted has a new identifier, so no deletion record of the earlier group names it (the
design paper 3.1).

**The group is where the plugin writes, and not what it reads.** A record counts wherever it sits
(§621), so the group decides where a new record is put, what the tab lists (§636), and what the
guard covers (§635).

**Which group the plugin writes into** (S11). A write puts what it adds into the group, among
the groups with the prefix that sit outside the recycle bin, that directly holds the most
records under the store key; where several hold as many, into the group among them with the
lowest identifier. Where no such group holds a record under the store key, the write makes a
group of the plugin's own under the root. So does the Create that starts a store, also where
the group of an earlier store is still in the tree (§632). A group that a file from outside
brings holds no record under the database's key, so it is never written into for carrying the
prefix. It rules out writing into the first group found, which a merged file could supply, and
into a group in the recycle bin, where Empty Recycle Bin would delete what the plugin had just
written. This is the single place where the plugin looks at which group is the recycle bin: it
decides where an entry is put, and nothing about what is read. *Held by*
`StoreWritesInRealDatabaseTests`: a second group with the prefix that holds no record is not
written into; a group in the recycle bin is not written into; and a store whose group was
deleted into the bin gets a new group at its next write, with every record in the bin still
counting. And for a write that is saved, by
`PairingInRealDatabaseTests.A_pairing_into_a_store_whose_group_is_in_the_recycle_bin_makes_a_group_and_takes_it_back_with_the_write`.

### A record §607

A record is an ordinary entry, written once.

| Field | As measured |
| --- | --- |
| Title | `KPG`, the kind, and the first eight hexadecimal characters of the identifier, separated by spaces. Nothing reads it (S14) |
| User name, URL | present and empty |
| Password | present, empty, protected |
| Notes | `KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.` |
| Auto-type | off for the entry |
| `KPG.Kind` | the kind of record: `authority`, `retired`, `client`, `revoked`, `profile`, `removed`, `version` or `head`. The kinds of §648, §649 and §650 were not measured: `withdrawn`, `kept` and `format` (S23, §658) |
| `KPG.Data` | the content: a single flat JSON object whose values are all strings |
| `KPG.Mac` | the check, §608 |
| `KPG.S.0001` and on | on a summary entry only: an item for each decision it restates, §611 |

**Custom strings are the carrier** because every program that was run keeps them, with their
values and their protection (the design paper 2, T13). `KPG.Data` is protected in an authority's
entry and in a retirement that holds the store key, and in no other record.

**The texts a person reads** (S14) are the measured texts, with a mark on each entry that
carries the store key. A record's title is `KPG`, the kind and the first eight hexadecimal
characters of its identifier, separated by spaces, and its notes are the notes of the table.
The group's name and notes are those of §606. An entry whose content holds the store key, which
is every authority's entry and every retirement that holds the key (§632), carries the mark in
its title and in its notes: the title ends with a space and `(do not delete)`, and the notes
begin with a line of their own:

```text
Do not delete this entry. It holds the key of this database's KeePassGate store.
```

The texts are written with the entry and never changed by the plugin, so the mark is in place
on a retirement that holds the key from the write that made it. No text is covered by the
check or read by the plugin, so a later release may word them otherwise without a new format
number (§650). *Held by* `StoreRecordTests`: a record whose content holds the key carries the
mark in the title and in the notes, and a record without the key carries neither; and by
`StoreWritesInRealDatabaseTests`: an authority's entry, and the retirements of Delete and of
keeping an authority, which hold the key, carry the mark with their content protected, and a
record whose title and notes were changed reads as it did.

**`KPG.Data` is read strictly**: an object, every value a string, and nothing else. The measured
code wrote it with its members sorted by name and no white space. Nothing reads that order: the
check is over the text as it is stored. **A member that appears twice makes the version fail**
(this document's own): the probe's reader kept the last of two, which lets two readers take two
contents from a single text that passes, and `JsonBody` refuses a repeated name today. An item of
a summary entry is read the same way. Every record holds a member beside its own: `nonce`, 16
random bytes as 32 hexadecimal characters. The nonce is what gives every act of writing an
identity of its own: a rule set, taken away and set again to the same text is a new record, so no
record can arrive already replaced under an earlier identifier (the design paper 3.3). The probe
derived its nonces from a fixed text, to make its runs repeatable; the product draws them at
random (S2).

**No record holds a marker of its form** (S1). The measured code wrote a member `v`, as `2`, in
every record and read it nowhere. The product writes no such member: the format number of §650
is the only statement of the store's form, and a constant beside it in every record is a second
number for a reader to give a meaning to. No count rests on the member, since nothing read it.
*Held by* `StoreFormatTests`: the vectors of the measured code, which hold the member, pass,
and a record composed without the member passes and reads alike.

**A reader passes over a member it does not know, in a kind it knows** (S26). A content or an
item may hold members beside those its kind names, and a reader takes nothing from them. That is
what lets a later release add a member that an earlier reader may ignore, without a new format
number (§650), and it is what lets the measured code's records, with their `v`, read. It rules
out a reader that fails a version for an unknown member, under which every added member would
be a new format. A member a reader must not ignore is added only with a new format number.
*Held by* `StoreReadingTests`: a record with a further member counts, and counts for what it
counted for without that member.

**What a reader refuses of a version, whole** (S13). A version fails, and is never read in
part, where a string of the store in it is longer than 1,048,576 characters, where its content
or an item holds more than 32 members, or where an `after` is anything but identifiers of 32
lowercase hexadecimal characters each, separated by single spaces, or empty. The first two are
the ceilings of the reader the wire's bodies are read with, which is the reader of a content
today.
A list cut short would leave out what it names as replaced or restates as refused, so a version
beyond a ceiling counts for nothing: an earlier version in the history may still pass (§622),
and an entry of the store with no version that passes is damage (§621). A string is held to its
length before anything of it is hashed or parsed. What the plugin itself writes stays well
inside these ceilings, §643. *Held by* `StoreReadingTests`: a version at each ceiling counts,
and the same version a character, a member or an item beyond it fails whole and stops the
store.

**A list inside a member is a string of items separated by single spaces**: identifiers in
`after`, names in `profiles`.

**No record is ever changed by the plugin**, with a single exception that changes no content:
setting an edited record back to the version the plugin wrote (§628).

### The check §608

`KPG.Mac` is an HMAC-SHA256 under the store key, written as lowercase hexadecimal, over this text
in UTF-8:

```text
<KPG.Kind> LF <KPG.Data>
  then, for each string whose name begins KPG.S. in ordinal order of the names:
LF <name> LF <value>
```

**The text has to read a single way** (this document's own; the probe took any name that begins
`KPG.S.` and any content). The parts are joined by line feeds, so a version passes only when none
of its parts holds a line feed: the kind holds none, `KPG.Data` and every item are JSON written
without white space between its parts, in which a line feed inside a string is escaped, and every
string whose name begins `KPG.S.` is named `KPG.S.` and exactly four digits. A version that holds
any other string beginning `KPG.S.`, or a line feed in a part, fails. Without that condition two
different sets of strings can give the same text, and so the same check. **The kind need not be
a kind the plugin knows**: a version of another kind passes as well, and what follows from it is
§621.

A record that takes access away carries the check as much as a record that grants. A hand edit of
a revocation makes a newer version that wins a merge, so a reading that left refusals unchecked,
or passed a failing refusal over, ended wider in 486 of 2,880 cells
(`measured-edited-refusals` 5.3, 5.6).

**It is a MAC and not a signature by the authority's key** (the design paper 7). A signature
would tie every rule and every profile name to a single authority, so that Recreate had to write
them all again, and it buys nothing, since nobody checks a record but a gate that holds the
database open.

**What the check is against, and what it is not.** A file that has never held this database's
store key cannot make a record that passes under that key. What was measured of that is narrower
than an earlier wording of this section had it: in the model, an entry planted under a key the
file does not hold was passed over (`measured-store-as-a-whole` 2.1). The model decided which
keys were the database's own with knowledge the reading does not have, and no planted entry
carried a key of its own. A file from outside that brings a store key of its own, with records
that pass under it, was in no run. The measured reading would have held that key beside the
database's and counted every record under it. §649 is the answer: records under more than a
single store key stop the gate until the operator keeps a store, so a file from somebody who
does not hold the database's key adds no record that counts. Whoever can open the database holds
its key and can write any record. The check is against a file from outside and against an
accident, and not against the holder of the master key (§639).

**The check covers text and nothing else** (read from the measured code, not run): not the title,
not the notes, not the entry's place, and not whether a string is protected in memory. A string
whose protection was taken off shows in clear in KeePass's entry view, and the check still
passes. The plugin protects such a string again at its next own write (S18, §628).

### The identifier §609

A record's identifier is made of its content, which is how a copy is told from the record.

```text
bytes  1 to 3    the first 3 bytes of HMAC-SHA256(store key, "tag" LF kind)
bytes  4 to 8    the first 5 bytes of HMAC-SHA256(store key, "subject" LF subject)
bytes  9 to 10   a number, big-endian: the height for a summary entry, the format number for a
                 version record (§650), the tag of the profile's name for a version of a
                 profile's rule (below), and 0 for every other kind
bytes 11 to 16   the first 6 bytes of the check
```

| Kind | Subject |
| --- | --- |
| `authority` | `authority:` and the authority's `id` |
| `retired` | `authority:` and the retired authority's `id` |
| `client`, `revoked` | `client:` and the client key |
| `profile`, `removed` | `profile:` and the name |
| `version` | the object it is a version of. As measured: `rule:` and the node's identifier as 32 hexadecimal characters, or `renew:` and the client key. A rule's object is now a node and a profile, and the switch's a node (§617): for a profile's rule the subject is the object's text before its first `/`, which is `rule:` and the node, and for a switch and an answer to a renewal the whole object (S8, below, and §618) |
| `head` | `store` |
| `withdrawn` | `certificate:` and the admission key of the certificate taken away (S23) |
| `kept` | `key:` and the name of the store key not kept, §649 (S23) |
| `format` | `format:` and the format number in decimal (S23) |

§658 has every kind with what the core reads of it.

**An entry is a record only under the identifier its content gives.** The reading computes the
check from the entry's strings, then the identifier from the kind, the subject, the number of
bytes 9 and 10 and that check, and compares it with the entry's own. A copy made by Duplicate
Entry, Duplicate Group, copy and paste, or an import with Create new IDs holds the strings under
another identifier, fails that comparison, and is passed over (§621).

**Every version of an entry is compared with the identifier of the entry that holds it** (as
measured): the current strings and each item of the history alike. An identifier that a history
item carries for itself is never read.

**The first eight bytes are for deletion records.** A deletion record is an identifier and a time
and nothing more. With this layout the deletion record of a record still tells a gate that holds
the key what kind of record it was and what it was about, and for a summary entry how high
(§623). The tag and the subject hash are keyed, so they mean nothing to anybody without the key,
and a file made outside cannot carry a deletion record that names a record of this store (the
design paper 3.2, 3.3).

**A version of a profile's rule carries a tag of the profile's name** (S8; the form that item
proposed from a measurement, `measured-rules-per-profile` 1 and 8, run since with the reading it
needs, `measured-rules-owed-forms` 6). Its object is `rule:`, the node's identifier, `/` and the
profile's name (§617). Its subject is the object's text before the first `/`, which is `rule:` and
the node, and bytes 9 and 10 hold the first two bytes of HMAC-SHA256 under the store key over a
label, a line feed and the name, where the name is the object's text after that `/`. **The label is
the name of no kind, and the format's description holds it** (§650; this document's own). The
measured code keyed the tag over `profile`, which is a kind, so the text hashed began as the text of
a check of that kind begins (§608); a label that no kind has keeps the tag's text apart from every
check. An object of type `rule` that holds no `/` has the whole object as its subject and 0 in those
bytes, as every other version has, the probe's `ident` doing the same. A switch's object, `switch:`
and the node's identifier, is such a subject, with 0 in those bytes. **A tag is two bytes of a keyed
hash, so it can be 0, and is a tag all the same.** Measured (`measured-rules-owed-forms` 6): the
probe's reading took 0 in those bytes for a version of a node's whole rule, which the product does
not write, so the deletion record of a row whose name's tag is 0 told of no row, and the row read as
a rule never made, with the store stopped for the summary entry lost with it.

**What the ten keyed bytes of a version's identifier cannot tell apart** (this document's own).
The tag of the kind, the subject hash and the number tell a node and a tag, and no more. They
do not tell a row from the row of another profile at the same node whose name has the same
tag, and they do not tell an object `rule:` and a node, with no `/`, from a row at that node
whose name's tag is 0. So a deletion record is held to tell of each of them that the file
knows (§617), a version names apart, in `lost`, the lost versions at its place it settles for
its own object, which it cannot tell were its own (§617), and a write adds a deletion record
for an identifier that nothing carries only where those ten bytes are the bytes of the naming
version's own object (§627).

**Why the tag.** The run wrote a row's version with the subject hash over the whole object. In
that form a deletion record tells the row only by what S8 ruled out: the reading tries each node
of the tree with each profile name the file knows, 12,108 keyed hashes for a tree of 2,000 nodes
and four names, and more with every node, ruled or not. With the tag, a hash for each node and a
tag for each name tell the same row: 4,048 keyed hashes for that tree, about half the time of
the read in the probe, and the same lines as the form run in 4 of 4 sequences, 24 of 24 lines.
**The switch keeps the form that was run**, a subject of its own and 0 in bytes 9 and 10. S8
proposed the switch under the subject of the node's rows with a fixed value of its own in those
bytes, which saves a hash for each node and was not run; the form taken is the form every count
of the switch is of. **Two names whose tags meet** under a key, by chance about once in 65,536
for each pair of names, are still two records, told apart by their checks, and a deletion record
that tells of a version of either row tells of both, so both rows refuse, which is narrower
(§617). That was reasoning, and owed a run, while the probe's reading kept a single name for a
tag. Run since, with a reading that tells of every name of a tag
(`measured-rules-owed-forms` 6): both rows refused and no read was wider, where the reading as
first run was fail-open for the name it did not keep. So the form is supported with that
reading, and with no other. **A version names what it replaces apart from the lost versions it
settles** (§617; measured, `measured-lost-settled`): a deletion record of a version a row
replaced, which the version that replaced it names in `after`, refuses neither row, and a lost
version a row settles in `lost` refuses the other row until that row settles it too. Among the
names a file knows, some pair meets with a chance of 0.75% for 32 names, 7.3% for 100 and 39%
for 256, and a meeting costs something only where a version is lost with no summary entry
restating it. The row of `version` in §658 holds the form, and the format number stays as it
is (§658). *Held by* `StoreFormatTests`, `StoreRecordTests` and `StoreKindsTests`, and for the
reading by `StoreInForceTests`.

**The height has two bytes and stops at 65,535** (S12). A summary entry's height is a whole
number from 1 to 65,535, in its content and in its identifier alike, and a write whose height
would be higher writes 65,535. Height decides nothing between two records (§611), two gates that
write apart give their summary entries the same height already, and what tells two summary
entries apart is the check, so summary entries that share the highest height read as any others
do. No write is refused for the height, and a deletion record of a summary entry still tells
how high, up to that number. It rules out a third byte, which the identifier has no room for
without taking it from the subject or the check, and a refusal at the last height, which would
stop a store for its age alone. A store reaches that height only past the ceilings of §643, so
only after a compaction exists. *Held by* `StoreReadingTests`: a summary entry of height 65,535
that names a summary entry of the same height is in force, and a content whose height is 0,
above 65,535 or not the number in the identifier fails.

**The identifier holds six bytes of the check** (S31), as measured, and those bytes are all that
binds an entry's content to its identifier. That is enough against a copy, and against anybody
who does not hold the store key, who cannot compute a check at all. It does not bind against
somebody who holds the key: that person can search, away from the database, for another content
whose check begins with the same six bytes, which is about 2^48 tries for a chosen entry, and
put it into the entry in place of what the plugin wrote (reasoning). At a billion tries a second
that is about three days of computing, so against that person the six bytes are a delay and no
barrier (reasoning; no rate was measured). With the file in hand that person writes any record
anyway (§639). The search matters for somebody who holds the key and can do no more than change
fields. From 0.3.0 on no route gives a client either (§633). What is left is a client of a 0.2
gate whose rule reaches the plugin's group with `read`, the `protected` switch and `update`
(§654): that client holds the authority's private key as well, and the search would let it
write an admission for a certificate of its own into an existing record. The order of the move
leaves no 0.2 gate serving, and granting on groups and not at the root closes it (§645).

**Six, and not more.** More bytes of the check have to come from the subject hash or from bytes
9 and 10. The subject hash is what lets a deletion record tell what it was about, among the
subjects the file knows (§623), and with fewer bytes two subjects of a database meet by chance.
Bytes 9 and 10 hold a summary entry's height and a version record's format number, which a
deletion record has to tell (§623), and the tag of the profile's name in a version of a
profile's rule, which a deletion record has to tell as well (above). This paragraph had the
form of a rule's version as a form that may come to use those bytes (S8), and it does. So the
layout is the measured layout for every kind, those two bytes of a rule's version aside, and
every count of this document is of the measured layout: a count of rules is of the subject hash
over the whole object, but where its sentence names the tag. *Held by* `StoreFormatTests`,
against the measured code's own identifiers, and by `StoreReadingTests`: an entry whose
identifier differs from the identifier its content gives in any single byte is no record.

### The order of decisions §610

"Later" is decided by descent and never by a clock. A record that replaces something names what
its writer had read of the same subject, in a member `after`: the identifiers of those records
and everything they name in turn. What a record names is replaced by it. What nothing names
stands. Two records of which neither names the other were made apart, in two copies, and together
they give the narrower answer: what both allow.

The list is closed and has no limit. The design paper cut it to the 64 nearest ancestors, and
under that cut a copy 65 writes behind ended wider in the model, where without the cut it is
refused (evaluation 4.1).

The laws of that combination held in every random pair and triple tried, and merges alone
converged in every history (`measured-store-as-a-whole` 2.1).

### The summary entry §611

With every write the plugin adds a summary entry and deletes, with a deletion record, every
summary entry it replaces (§627).

| Part | As measured |
| --- | --- |
| `KPG.Kind` | `head` |
| `KPG.Data` | `v`, `nonce`, `height` as a decimal number, and `after`: the identifier of every summary entry this entry replaces |
| `KPG.S.nnnn` | an item for each decision restated, a flat JSON object of strings. The items are sorted by their text and numbered from `KPG.S.0001` with four digits, so a summary entry holds at most 9,999 (S13, §643) |
| Identifier | as every record's, with the subject `store` and the height in bytes 9 and 10 |

**The height** is 1 more than the highest height the writer has seen: among the summary entries in
the file, and among the deletion records that name a summary entry, read off their identifiers.
Height decides nothing between two records.

**`after` names** every summary entry present in the file, everything those name, and every
summary entry a deletion record in the file names.

**What an item restates:**

| Item | Members | Restates |
| --- | --- | --- |
| `{"kind":"revoked",...}` | `client` | a revocation of that client key |
| `{"kind":"revoked",...}` | `hash` | a revocation whose client the file names nowhere, by the first 16 characters of its identifier (below) |
| `{"kind":"retired",...}` | `authority` | a retirement of that authority |
| `{"kind":"retired",...}` | `hash` | a retirement whose authority the file names nowhere, by the first 16 characters of its identifier (below) |
| `{"kind":"removed",...}` | `name`, `id` | the removal of a profile name, with the identifier of the removal's record |
| `{"kind":"removed",...}` | `id` | a removal whose name the file names nowhere, by its identifier (below) |
| `{"kind":"version",...}` | `id`, `object`, `after`, `doc` | a standing version of a rule, of a node's switch or of an answer to a renewal, whole, with its text. With a rule for each node and profile there is an item for each profile's standing version at each node (§617) |
| `{"kind":"withdrawn",...}` | `id`, `certificate`, `renewed`, and `from` where the record holds it | a record that took a certificate away, with that record's identifier and what the record holds, member for member (§648) |
| `{"kind":"kept",...}` | `id`, `key`, `after` | a keep record, with its identifier, the name of the key not kept and the keep records it replaces (§649) |
| `{"kind":"format",...}` | `format`, `plugin` | the highest format number and the highest plugin version the store states (§650). Every summary entry holds exactly this single item of the kind |

It restates every revocation, every retirement, every removal and every standing version. It
holds no authority, no admission and no profile addition, because losing any of those admits
nobody. Each alone, the records or the newest summary entry, is enough to compute what takes
access away, and the reading unites them (§623).

**An item with no subject** (built; as measured in `papers/ground/measured-seal-restates.md` on
`spikes/storage-survival`, the form named `hashes` there, before anything of it was built). A
revocation, a retirement or a removal of a name that a deletion record under the store key tells of,
and whose subject no record and no item of the file names, cannot be restated by its subject: the
subject is a keyed hash in the deletion record's identifier, and nothing in the file gives it back.
A summary entry restates such a decision by an item that names no subject: a revocation or a
retirement by `hash`, the deletion record's keyed tag and subject hash, the first 16 lowercase
hexadecimal characters of its identifier (§609); and a removal by `id`, the deletion record's
identifier, 32 lowercase hexadecimal characters, an item holding anything else in `id` not reading
as its kind, since an addition of the name has to name each removal of it in `after` (§616). Several
deletion records of a single decision are a single item. Such an item tells its decision of every
subject the file names whose keyed tag and subject hash it holds, as a deletion record does, so a
subject named again later, an admission a merge brings back among them, is refused by its hash; it
restates every record of its kind whose identifier begins with its `hash`, or the removal whose
identifier it holds; and it counts as a carrier of the decision wherever a deletion record counts,
an addition of the name naming such a removal in `after` among them. Every write carries it while
the reading the write is planned over names the subject nowhere, a write that adds a record naming
the subject among them, as the measured form has it: restated by that subject in the same write, the
item would stop telling of any other subject whose hash it holds, which the reading side never does.
Once a record or an item of the file names the subject, the write restates the decision by its
subject and carries the item no further. *Held by* `StoreInForceTests`, the reading of each item,
`StoreWriteTests`, what a write carries and when it restates by the subject, and `StoreKindsTests`,
the form.

**What the kinds that were not measured add to it** (proposed; the items are those of the table,
S23). Each of them takes something away or holds a plugin back, so each is restated: a
certificate taken away, by an item for each record that took it away, which holds that record's
identifier and what the record holds (§648); a keep record, by an item for each (§649); and the
highest format number and the highest plugin version the store states (§650). **An item of a
kind the table does not hold stops the gate**, as a record of such a kind does (§621).

Without the summary entry as a second carrier, 20 of 20,000 histories failed the model's check,
against 2 with it (evaluation 4.1).

### What is nowhere §612

- **No custom data**, of the database, of a group or of an entry.
- **Nothing in the operator's own entries or groups, ever, for any purpose**, and no mark on them
  (decision 10). The design paper put a reference on every node that carries a rule, by which a
  copy was held to its original's rule and a node whose rule was lost refused. That mark is left
  out: a plugin string on an entry is undone by that entry's dialog when the plugin writes while
  the dialog is open (`measured-desktop-windows` 4.2), and a reference on the operator's entry was
  gone after an ordinary edit of that entry in another copy in 6 of 8 cells (evaluation 7).
- **No record placed inside the operator's groups.** The plugin's records stay in the plugin's own
  group. No entry is put into a group of the operator's to make a copy of that group detectable.
- **No deletion record that names no entry** (decision 15). The design paper wrote such a record
  beside every revocation and every retirement, as a third trace of the decision, and called it a
  witness. §639 states what its absence costs.
- **No time and no place decides anything.** Not a modification time, not which group is the
  recycle bin, not where a record sits.
- **Nothing outside the file** (decision 2): no file beside the database, no digest, no memory of
  any kind that outlives KeePass. So nothing detects a file that was rolled back (§639).
- **Nothing of the store needs more than KDBX 4.0.** Keepass2Android and KeePassDX, each as its
  own code compiled for the desktop, write an AES-KDF file with nothing above 3.1 as KDBX 3.1
  with entries and protection kept, and KeePassLib writes it back as 4.0
  (`measured-source-harness` 5). That is a claim about the store and not about every file that
  holds it: a database that 0.2 wrote keeps its custom data keys, each with a time, and stays a
  4.1 file for as long as they are in it (§645), and so does a file for anything else in it that
  is 4.1, `CA.md` §204. **Measured in the product**: a saved database that holds an authority, an
  admission and a profile name in the store, and nothing of the plugin's in its custom data, is
  a KDBX 4.0 file (§616). **And through the pairing's own write**, saved by KeePass, with the
  version read from the file's header: an authority and a client paired for no profile is a 4.0
  file; paired for a profile, which the write adds to the list, a 4.0 file; and paired for no
  profile with a standing answer to its renewal beside it, a 4.0 file. So neither an admission,
  a name nor an answer moves the file's version, where an answer kept in the custom data took
  the file to 4.1. *Held by*
  `AdmissionInRealDatabaseTests.An_admission_and_a_profile_name_in_the_store_leave_the_file_KDBX_4_0`
  and
  `AdmissionInRealDatabaseTests.A_pairing_leaves_the_file_KDBX_4_0_whatever_it_names_and_whatever_is_refused`.

---

## 3. Each kind of record §613

The members named are those the measured code wrote and read, beside `v` and `nonce`. Where the
product's own content differs from the probe's stand-in, the paragraph names the item of §646.

### What the core reads of each kind §658

The core of the store reads, checks, orders and restates the records of every kind, and stops
for a record of a kind it does not know, without knowing what a kind does to access. This table
is what it needs of each kind (S23). The kinds, the subjects, the members and the items of the
kinds that were measured are as measured. Whatever else a kind holds, and everything it means,
is left to the pull request that brings its meaning, under the item the last column names.

| Kind | Subject | Members the core reads | Beside a deletion record naming it | Restated by | Left to |
| --- | --- | --- | --- | --- | --- |
| `authority` | `authority:` and `id` | `id`, `storeKey` | out of force, and its key still found (§621) | nothing | nothing: §614 |
| `retired` | `authority:` and `authority` | `authority`, and `storeKey` where it holds the key | counts | an item `retired` with the same `authority`, or an item `retired` with no subject whose `hash` the record's identifier begins with (§611) | nothing |
| `client` | `client:` and `client` | `client`, and `certificate`, `der`, `issuer`, `profiles`, `subject`, `admitted` and `from` where it holds them | out of force | nothing | nothing: §615, §648 |
| `revoked` | `client:` and `client` | `client` | counts | an item `revoked` with the same `client`, or an item `revoked` with no subject whose `hash` the record's identifier begins with (§611) | nothing: §615 |
| `withdrawn` | `certificate:` and `certificate` | `certificate`, and `renewed` and `from` where it holds them | counts | an item `withdrawn` whose `id` is the record's identifier, with the same `certificate`, `renewed` and `from` | nothing: §648 |
| `profile` | `profile:` and `name` | `name`, `after` | out of force | nothing | nothing: §616 |
| `removed` | `profile:` and `name` | `name` | counts | an item `removed` with the same `name`, whose `id` is the record's identifier, or an item `removed` with no subject whose `id` is the record's identifier (§611) | nothing |
| `version` | the `object`; for an object of type `rule`, its text before the first `/`, with the tag of the text after it in bytes 9 and 10 (§609) | `object`, `after`, `doc`, and `lost` where it holds it | out of force | an item `version` whose `id` is the record's identifier, with `lost` where the record holds it, where no version in force names the record in `after` | nothing: §617 for a rule and a switch, §618 for an answer to a renewal |
| `kept` | `key:` and `key` | `key`, `after` | counts | an item `kept` whose `id` is the record's identifier | nothing |
| `format` | `format:` and `format` | `format`, `plugin` | counts | the item `format` of a summary entry that states a format number at least the record's | nothing |
| `head` | `store` | `height`, `after`, and every item | out of force | nothing | nothing |

**The forms of the members.** `id`, `authority`, `client`, `name`, `object` and `doc` are texts
the core compares and does not look into, and so are the `client`, `issuer`, `der`, `profiles`,
`subject` and `admitted` of `client`. Beyond its type, the core looks into an object in a single
place: to make the identifier of a version whose object is of type `rule`, it splits the text
at its first `/` into the subject and the name the tag is of (§609), and holds neither part to a
form. **Where the core takes an admission key from a text, it
holds the text to that form** and passes over a text out of it, which stops nothing: the
`certificate` and the `from` of `client`, and the `from` of `withdrawn` and of its item, are an
admission key only as 64 lowercase hexadecimal characters, and `renewed` is split on single
spaces into texts held to the same form (§623 step 2). The `hash` of an item with no subject is
16 lowercase hexadecimal characters, the first eight bytes of an identifier (§609, §611); an
item `revoked` or `retired` that holds neither its subject nor a `hash` in that form, and an item
`removed` that holds no `id`, are no items of their kind. `storeKey`, and the `certificate` of
`withdrawn` and of its item, are 64 lowercase hexadecimal characters, and `key` is the name of a
store key (§649). `after`, and the `lost` of `version` and of its item, are lists of
identifiers (§607), and a `lost` out of that form fails its version as an `after` does (§617).
`format` and `height` are whole
numbers from 1 to 65,535 in decimal, with no sign and no leading zero, and `plugin` is three
such numbers, each from 0, joined by periods.

**The members a kind's meaning reads** are in its row, each held where it is present and as a
text, whatever form its meaning gives it, and kept with the checked form as the members above
are (§620), so none of them decides whether a version reads as its kind. What a content that
lacks such a member, or holds it out of form, comes to is decided by the kind's meaning and never
by a stop. They are, for `client`: `certificate`, `der`, `issuer`, `profiles`, `subject`,
`admitted` and `from` (§615, §648). The core reads them because a deletion record tells of a
certificate taken away by the admission keys that admissions name (§623 step 2), and a write
deletes a record of kind `client` by its `client`, its `issuer` and its `certificate` (§627
step 3), each by comparing texts and reading no certificate (S23). It keeps `der`, `profiles`,
`subject` and `admitted` with them, as texts between holds, because the reading of an
admission's meaning (§615) uses each of them at every hold; a member that reading does not use
is not kept by the core. It rules out a form held by the core, under which an admission with a
member out of form would be damage and stop the store, where §615 has it admit nobody.
The `renewed` and the `from` of `withdrawn` and of its item are read by the core where they are
present, since an item restates a record only with the same of each (§648); a record or an
item that lacks either still reads as its kind, and what each means is decided there. An
authority's document is not among them: it holds private keys, so it is read from the version's
own content where a gate needs it, and is kept nowhere as text (§614, §632). *Held by*
`StoreKindsTests`, which holds the row, and `StoreReadingTests`: a record of kind `client`
holding `client` alone, or a member beside it out of the form its meaning gives it, reads as
its kind and stops nothing.

**A version reads as its kind** where its content holds every member its row names, in these
forms. A version that passes its check and does not read as its kind fails, as the measured
reading failed a content without the member its subject is made of, and what follows is the
history rule and then damage (§622). A summary entry reads as its kind only with exactly a
single item `format`.

**The object of a `version` has a type**, which is the text before its first colon: `rule` for a
profile's rule at a node, `switch` for a node's switch, and `renew` for an answer to a renewal.
The measured code wrote `rule` and `renew`. **A version, or an item `version`, whose object is of
another type is of a kind the plugin does not know**, and stops the gate as §621 has it (this
document's own). Without that, a later release could add a kind of object, as a record of the
recycle bin would be (§619), that an earlier plugin read as a rule about nothing wherever the
format number was lost.

**The versions of an object are worked out by the object's text, whatever its type** (this
document's own): which versions of it stand, and whether a version of it is present beside a
deletion record while none stands (§623 step 3). What a deletion record tells of a version
whose content is nowhere is stated with each type: in §617 for a rule and a switch, and in §618
for an answer to a renewal. **An object of a type the core knows, in a text that type's meaning
does not read, is the object of nothing**: its versions are records, restated, counted by the
room and deleted as any are, and no rule is read from them (§617). It rules out a store stopped
for an object out of form.

**What is left to a later pull request is what a kind means, and never whether it is read.**
The text of an object after its type and the text of `doc` are fixed by the pull requests that
build those kinds. The members of an authority and of an admission beside those named here,
and the renewals a `withdrawn` record names, are fixed in §614, §615 and §648. A member that a
kind's meaning comes to read is named in its row, which changes the row (below); a member that
no row names is passed over by the core and makes no record damage (§607, §615). This
paragraph left the row of `version` to the pull request that builds the rules, should the form
of a rule's version take bytes 9 and 10 of its identifier (S8). It takes them (§609), and the
row above holds the form.

**How a kind comes to the table.** The table holds every kind of the first format, from the
pull request that builds the core on, so that no pull request before release 0.3.0 has to add a
kind in order to bring a meaning. Until that release, a row may still be added or changed by the
pull request that needs it, and the format number stays 1: no released plugin has written or
read the first format, and nothing is owed to a store that a build before the release wrote
(§645). From that release on, a new kind, a new kind of item, a new type of object or a changed
row is a new form of the store, and moves the format number (§650).

*Held by* `StoreKindsTests`, which holds the table, the layout of §609 and the text of §608 to a
description written beside the format number (§650), and by `StoreFormatTests` for the tag and
the subject of each kind that was measured, against the measured code.

### Authority and retirement §614

**An authority** is a record of kind `authority`, protected, holding today's authority document
(`CA.md` §222: the authority, the host certificate and the gate key) and the store key. As
measured its members are `id`, the authority's name, and `storeKey`, the key as 64 hexadecimal
characters. The probe's authority held no certificate.

**The authority's content** (S3) holds these members beside `nonce`, each written as the section
that defines it writes it:

| Member | Holds |
| --- | --- |
| `id` | the SHA-256 over the authority's certificate, as 64 lowercase hexadecimal characters, which is what the product files an authority under today, `CA.md` §204 |
| `storeKey` | the store key, as 64 lowercase hexadecimal characters, as measured and as the core reads it (§658) |
| `ca` | the authority's certificate and its key, base64 of a PKCS#12 with no password, `CA.md` §222 |
| `host` | the host certificate and its key, in the same form |
| `sniKey` | the gate key, base64 of its 32 bytes, `CA.md` §222 |

The names `ca`, `host` and `sniKey` are today's. The store key stays hexadecimal beside the
base64 of the others, since the core finds a key by that form and no reader converts a member
of either. No member states a version: the format number is the only statement of the store's
form (§607).

**An authority is whole** where `ca`, `host` and `sniKey` read as `CA.md` §222 has it, each key
is its certificate's own, `id` is the digest of the certificate in `ca`, and the host
certificate chains to that certificate by the anchor pin, `CA.md` §214. **An authority that
passes its check and is not whole is damaged**, as an authority that will not decode is today:
it stands, its store key is found, it is never served and never kept, and a retirement names it
by its `id` as any other, so Recreate, Delete and keeping another authority retire it. It rules
out reading such a record as no record, under which a store whose only carrier of the key is
damaged would hold no key and start again (§632). It rules out serving an authority filed under
an `id` that is not its certificate's, which no admission's `issuer` could be held to (§615).
The document is read from the version's own content where a gate builds what it serves with,
and is kept nowhere as text between two holds (§632). *Held by* `StoreAuthoritiesTests`: an
authority whose `id` is not the digest of its certificate, whose host does not chain, whose key
is not its certificate's, or which lacks a member, is damaged, gives its key, is not served,
and is retired by Recreate; and by `StoreRecordTests` against the measured code for the
content's text, its check and its identifier.

**A retirement** is a record of kind `retired` with the member `authority`, the `id` of the
authority it retires. It is final: nothing takes a retirement back. A retirement written by
Delete, or by keeping an authority, also holds `storeKey`, and is then protected (§632).

**What each command writes.**

- **Create**, where no authority stands: an authority under the store key that stands, or under
  a new key where no key stands. The Create that starts a store also makes the group (§606) and
  writes the version record (§650). Create is refused while the store is stopped (§624), while
  several store keys stand (§649), and while the plugin is held back or the store waits to be
  raised (§652).
- **Recreate**: a retirement of the authority served, and a new authority that holds the same
  store key. Where several authorities stand, a retirement of each of them, as today.
- **Delete**: a retirement of the authority served that holds the store key. Where several
  authorities stand, a retirement of each of them, each holding the key.
- **Keeping an authority**, where several stand: a retirement of each authority not kept, each
  holding the store key (below).

The entry of a retired authority is deleted with a deletion record in the same write (§627),
which takes its private key out of the file (the design paper 9).

**Which authority is served** is decided by the retirements, as today, among the records under
the single store key that stands (§649). The authorities that stand are the `authority` records
that are not beside a deletion record naming them and that no retirement names, where a
retirement counts as an entry, as an item of any summary entry present, or as a deletion record
that tells of it (§623). A single standing authority, in a store that is whole, is served. No
standing authority serves nothing. Several standing authorities serve nothing, every connection
is answered `503 undecided`, and KeePass asks the operator which authority to keep, as today,
`CA.md` §204 (evaluation 6, row 20).

**Keeping an authority writes a retirement for each authority not kept, each holding the store
key** (S11), in a single write, as the measured code retired each authority not kept. The
entries of the authorities not kept are deleted in that write, as every retired authority's
entry is. An answer is about the authorities it was given among, as today: the write is
refused where the authorities that stand are any other set than the dialog listed, `CA.md`
§204. **Why those retirements hold the key** (reasoning, not run). Copies that each keep
another authority retire every authority between them, `CA.md` §204, and each copy deletes,
with a deletion record, the entry of the authority it did not keep. Once such copies are
synchronized every authority's entry is gone. With the key in the authorities' entries alone
the file would then hold no store key, which is a store started again, with every rule, name
and revocation to enter again (§632), where today such copies lose their clients and nothing
else. With the key in each retirement the store stays whole, no authority stands, and Create
makes an authority under the same key. Nothing is deleted in order to take anything away:
what is deleted is retired in the same write (§627). *Held by* `AuthorityCommandsInStoreTests`:
keeping an authority among several retires each of the others, each retirement holding the key;
and by
`StoreWritesInRealDatabaseTests`: copies that keep different authorities, synchronized, hold
the store key, serve nothing, and take a Create under that key.

**A retirement under another store key retires nothing.** An authority and a retirement that a
file from outside brings under a key of its own are under a second key, and a second key stops
the gate until the operator keeps a store (§649). The measured code counted a retirement under
any key it held, which let a file from outside retire the database's authority by naming its
`id`, and that `id` is public where it is the digest of the authority's certificate (read from
the measured code, not run).

**What is built.** The authority's record and the retirement as this section has them, the
commands' writes, and which authority is served. `AuthorityRecord` composes and reads the
content, `StoreAuthorities` works out the authorities that stand and the authority served, and
`AuthorityService` writes Create, Recreate, Delete and keeping an authority as acts of §627,
each act held to the authorities that stood when it was composed. **Each command is answered by
its write, and nothing reads the store back after it** (this document's own). On the settings
dialog the buffer holds a press only where the plan of every act held with it takes the press,
and the page reads the store as that same plan leaves it, so a read back finds what the write
took and could refuse nothing the write did not, while a refusal after the write would leave
the press held for OK. Keeping an authority from KeePass's main window applies the plan the
store made of a reading in the same turn, as the dialog's OK does. **A Create or a Recreate is
asked first whether its write would be taken and leave the store whole**, since a store left
without room serves nothing, the authority just made among it, and nothing on the tab lifts that
(§643). Its records are planned over the store as the page reads it and applied in memory, an
authority with an `id` of a digest's length standing in for the authority not yet minted, and
the store they leave is judged. Where the write would be refused or leave the store held back,
the press is refused before anything is minted, written or held, and the page withholds the
button with a line naming it and why, and where the store would be left full, that nothing on
the tab makes room, in the place of the line of a store past half its room. On the settings
dialog that store is the store as OK would leave it with the presses held, so a press is judged
as a write after them, as on the next dialog: OK writes them all in a single write, which names
a summary entry fewer, so a press refused there would have left room for a single write more at
the most. **The question is asked at the press and not at OK**: a write landing under the
dialog, as a pairing, can still leave the store full at OK, and OK writes the Create or the
Recreate all the same. **Delete and keeping an authority are not asked.** A store left without
room is stopped, and nothing the plugin offers lifts that (§643): a Delete that takes the last of
the room stops the store for good, and every Create after it is refused; keeping an authority
that takes the last of it leaves the authority kept in a stopped store, served nothing. *Held
by* `PendingRecordsTests`: a Recreate whose own write would leave the store full is refused with
nothing held, and a Create after a Delete, whose write would leave the store full, is refused
with the Delete left held; and by `AuthorityPanelTests`, which withholds each button with its
line. The admissions are records of the store as well (§615), and their removal is part of the
act: the write that retires an
authority deletes every record of kind `client` whose `issuer` is that authority (§627 step 3),
an admission that a pairing landed under the dialog among them, since the write is planned
against the store as it stands at OK. An act refused or left out at OK deletes none, and a
revocation is taken away by no command. A standing answer to a renewal stays where its client
has no admission left: nothing is removed for a client that is gone (§618). *Held by*
`StoreAuthoritiesTests`, `AuthorityCommandsInStoreTests`, `AuthorityServiceTests`,
`StoreAuthorityServedTests` and `PendingRecordsTests`, and in a real KeePass by
`LiveAuthorityTests` and `LiveAuthorityChooserTests`.

### Admission and revocation §615

**An admission** is a record of kind `client`, a record for each certificate admitted, holding
today's record text (`CA.md` §223). As measured its members are `certificate`, `client`, the
client key, `issuer`, the `id` of the authority that issued it, and `profiles`. The client key is
today's: the admission key of the certificate the client paired with, copied into every record a
renewal writes, `CA.md` §217.

**The admission's content** (S4) holds a member for each field of today's record, beside
`nonce`:

| Member | Holds |
| --- | --- |
| `certificate` | the admission key: the SHA-256 over the certificate, as 64 lowercase hexadecimal characters, `CA.md` §217 |
| `der` | the certificate itself, base64 of its DER |
| `client` | the client key, in the form of an admission key |
| `issuer` | the `id` of the authority that issued the certificate (§614) |
| `profiles` | the approved names, separated by single spaces, and empty for none: written each once and in ordinal order, as measured, and read as a set |
| `subject` | the subject as issued |
| `admitted` | the moment of the approval, as today's record writes it: RFC 3339 in UTC to the second, as `Moments.Wire` writes a moment. It is shown, and a record whose `admitted` does not parse is no admission; beyond that it decides nothing |
| `from` | on an admission that a renewal writes, the admission key of the certificate it was renewed from (§648). A pairing's admission holds no such member |

A member for each field, and not a single member holding today's text: a content is a flat
object of strings already, and a text of lines inside a string would be a second grammar for
the same fields.

**How two of the members are written** (S4). The writer writes each name of `profiles` once and
in ordinal order, as the measured code writes every list and as the admissions it composed hold
their names; a reader takes the names as a set, and neither their order nor a name repeated
decides anything. It rules out the order of approval, which no step reads and under which the
product could not compose what the measured code composed.
`admitted` is written in the single form of the table and read in that form alone, which is
today's form and the measured admissions'. It rules out a second spelling of a moment. *Held
by* `StoreRecordTests`, against the admissions of the measured code, and `AdmissionRecordTests`.

**An admission reads** where every member but `from` is present and in its form, `from` is in
its form where it is present, `der` decodes to a bare certificate whose digest is
`certificate`, and the names in `profiles` are read as today, a name that could not have been
issued as a claim being dropped, `CA.md` §223. The forms are those of the table: `certificate`,
`client`, `issuer` and `from` are 64 lowercase hexadecimal characters each, and `admitted`
parses in the form it is written in. **A member that the table does not name is passed over**
(S4), as a reader passes over a member it does not know in every kind (§607). A member that the
meaning comes to read is named in the row of §658, which changes the row, and a row changed
from release 0.3.0 on moves the format number (§650). It rules out today's refusal of a field
the record does not know, and buys no more than that: a member no row names does not make a
record damage. The reader stays total: nothing a content holds makes it throw. *Held by*
`AdmissionRecordTests`: an admission read back as it was written; each member absent, each out
of form, and a `der` whose digest is another certificate's or that holds a PKCS#12, reading as
no admission; and a member the table does not name passed over.

**A record of kind `client` that passes its check and does not read admits nobody**, which is
the answer for no record at all, and the clients page names it by its entry. **It is no
admission anywhere** (S4): it is counted among no admissions of a certificate (below), it is no
admission of origin that a `from` leads to, and no `renewed` names its certificate (§648). It
rules out counting such a record by the text of its `certificate`, under which a record that
does not read would take an admission's access away without being readable as a decision.
*Held by* `StoreAdmissionsTests`: such a record beside an admission of the same certificate that
reads leaves that admission admitting, in every order of entries.

**`issuer` is held to the anchor pin.** The gate judges a certificate against the
authority it serves by the pin, `CA.md` §214, before any record is looked up, and admits it
only where its record's `issuer` is that authority's `id`, so a record whose `issuer` disagrees
with the pin admits nobody. Nothing of this changes what a client is sent, `API.md` §411: the
bundle of a pairing and of a renewal is as it was. *Held by* `StoreAdmissionsTests`: a record
that lacks each member in turn, holds each out of form, or holds a certificate whose digest is
not `certificate`, admits nobody and stops nothing; a record whose `issuer` is another
authority's admits nobody under the authority served; and by `StoreWriteTests` against the
measured code for the content's text, its check and its identifier.

**A certificate is admitted** when its record is not beside a deletion record naming it, no
other admission in force names the same certificate (below), no revocation names its client
key, it was not taken away, no rule of §648 refuses it, no retirement names its issuer, and its
issuer is the authority served. Its profiles are the record's, less every name the profile list
does not hold, as today. The admission record stays load bearing: a certificate with no record
is refused.

**Two admissions that name the same certificate** are not what the plugin writes, and whoever
holds the store key can write them. The measured code let whichever it read last decide the
profiles, so the answer turned on the order of entries. **A certificate that several admissions
in force name is admitted by none of them** (S24). The answer does not turn on the order in
which entries are read, and it is never more than any single admission of them gives. It rules
out the profiles all of them hold, under which a record that somebody added beside the
plugin's own would still leave the certificate admitted. **An admission that a decision refuses
is not among them** (S24): an admission whose client is revoked, whose issuer is retired or
whose certificate was taken away. Such an admission is deleted at the plugin's next write
(below), and a deletion by the plugin changes no answer (§627), so the answer is the same
before that write and after it. The clients page shows such a certificate with every record
that names it, and the operator takes the certificate away or revokes a client that holds it:
the page then shows the certificate under the client that is left. **A record of kind `client`
that does not read is not among them either** (S24), being no admission (above). **A renewal
from such a certificate does not count** (S24): an admission whose `from` names a certificate
that several admissions in force name, and that was not taken away, is renewed from a
certificate admitted by none of them, so the fifth rule of §648 finds its origin neither
admitted nor taken away. Where that certificate was taken away, the rules of §648 decide alone,
as they do once a write has deleted its admissions. It rules out following the `from` of any of
those admissions, which would turn on the order of entries. *Held by* `StoreAdmissionsTests`:
several admissions for a certificate, read in every order, admit nobody, and the same
admissions with a deletion record naming all but a single admission admit by that admission;
revoking the client of an admission of a certificate that two admissions name leaves the
certificate admitted under the other, before the write that deletes the refused admission and
after it; a record that does not read beside an admission leaves the certificate admitted; and
by `StoreAdmissionChainTests`: a renewal from a certificate that several admissions name and
that was not taken away is not admitted, in every order, and a renewal from such a certificate
that was taken away is answered by the statements, as §648 answers it.

**A revocation** is a record of kind `revoked` that names the client's key, in the member
`client`. It refuses every certificate whose record carries that client key, in whichever copy
and at whichever time the certificate was issued, and it never expires and is never pruned
(decision 13). The measured code also wrote the member `certificate`, and wrote a revocation for
each certificate of the client that its copy admitted. **The product writes a single
revocation, holding the client key alone** (S5): the member `client`, beside `nonce`. Every
certificate under that client key is refused wherever it was issued, and the summary entry
restates the revocation by that key, so a member that names a certificate would state what no
step reads, and a record for each certificate would add entries and nothing else. A client
revoked again, in another copy or after its records came back, gets a further record of the
same subject, and each counts. *Held by* `StoreAdmissionsTests`: a revocation refuses every
certificate under its client key, a certificate renewed in a copy that had not read the
revocation among them; and by `StoreWriteTests` against the measured code.

**Why the key and not the certificate.** With a revocation that names a single certificate, a
renewal at a gate not yet told of the revocation escaped it: 15 to 28 histories of 20,000 a cell
ended wider. With the key, the renewed certificate was refused in every merge cell but an import
with Create new IDs, which brings no decision in (`measured-store-as-a-whole` 2.5, 3.1). The cost
is that a key has no date at which it expires, so a revocation and its item in every summary
entry stay for good (§643).

**A revoked client pairs again**, and is then a new client under a new client key, since a
pairing writes the new certificate's own key as the client key (reasoning, from `CA.md` §217).

**What replaces what.** Nothing replaces an admission. An admission whose client key is revoked,
whose certificate was taken away, or whose issuer is retired, is deleted with a deletion record
at the plugin's next write (§627), and admits nobody before that. **Where the issuer is retired
only by a deletion record**, and no record and no item names that authority, the admission is
not deleted, since an admission's `issuer` makes no authority known (§623 step 2); nothing
serves that authority either way. A record of kind `client` that does not read goes the same
way where its `client`, its `certificate` or its `issuer` names what is revoked, taken away or
retired, and stays otherwise. An admission lost in any way fails closed: that client pairs
again.

**A revocation takes a client away whole.** It is the operator's, made on the clients page. A
single certificate is taken away by a record of another kind, by its own client or by the
operator (§648). A record that a lost answer to a renewal left behind is cleared that way by
the operator: the client never held that certificate and cannot give it up, `PAIRING.md` §312.

**What is built.** The admission's record, its reading and its writes, and the revocation.
`AdmissionRecord` composes the content and reads it, with the identifier of the entry it was
read from. `StoreAdmissions` reads who is admitted from the reading of a hold (§620): a
certificate is admitted by a single admission in force that reads, whose client key no
revocation names, whose certificate was not taken away, that no rule of §648 about its chain of
origins refuses, and whose issuer no retirement names and is the authority served, in a store
that is whole. A record that does not read, and an admission that a decision refuses, are
counted among no admissions of a certificate (S24). The gate asks once for each connection,
from the reading every question of that hold is answered from, and keeps no answer.
`AdmissionActs` composes the acts of §627: an admission, held when the write is planned to its
issuer standing alone; a revocation that holds the client key alone and has no ground; and the
records that take a certificate away (§648). A pairing and a renewal write the admission by a
write that is saved before the certificate is handed over, the renewal's with `from`. The
operator's revocation, and the operator's removal of a single certificate, are pressed on the
clients page, wait in the tab's buffer and are written at the settings dialog's OK. Every
write deletes the admissions that a revocation, a retirement or a certificate taken away
refuses, and none for its age (§643).
The clients page, the Certificate Authority page, the Profiles page, the approval dialog and
the dialog that asks which authority to keep each read the same listing of a reading: every
admission in force that reads and that no decision refuses, each with whether it admits, is
under an authority that is not served, names a certificate that several admissions name, or is
a renewal that does not count (§648); the records that do not read; and how many clients a
decision refuses while their admissions wait for the next write. A row of the clients page takes
what its admission comes to from that listing, and reads `not this authority`, `listed twice` or
`not counted` where the listing has it admit nobody (§648). The standing answers to a renewal
are versions of the store about the client (§618), read from the same reading on the client's
own row. Nothing of this changes what a client is sent, `API.md` §411. *Held by*
`AdmissionRecordTests`, `StoreAdmissionsTests`, `StoreAdmissionChainTests`, `AdmissionActsTests`,
`StoredRenewalsTests`, `ClientListTests`, `AuthorityPanelTests`, `PendingRecordsTests` and
`PendingChangesTests`, and over real databases by `AdmissionInRealDatabaseTests`,
`PairingInRealDatabaseTests`, `RenewalInRealDatabaseTests` and `RevocationInRealDatabaseTests`.
Among them: an admission is an entry of the plugin's group, its content unprotected, with
nothing of it in the custom data
(`An_admission_is_an_entry_of_the_plugin_s_group_with_its_content_unprotected_and_unmarked`);
two clients are two entries (`Two_clients_are_two_entries_so_a_replicated_database_can_merge_them`);
an admission written while a group dialog is open survives that dialog's OK
(`SnapshotHazardTests.An_admission_written_while_the_dialog_is_open_survives_its_OK`); and over
copies brought together by `PwDatabase.MergeIn` a revoked client stays refused where the other
copy still holds its admission, whatever became of the entries' times, and an expired admission
that a merge brings stays and admits nobody (`MergeInRealDatabaseTests`).

### A single certificate taken away §648

**A certificate that its own client gives up gets a record of a kind of its own, which names
that single certificate** (a later decision). The certificate is refused from the next request,
the certificate a renewal issued keeps working, and the operator's revocation still names the
client's key and never expires.

**The operator can take a single certificate away on the clients page as well as revoke the
whole client, and the single removal writes the same kind of record** (a later decision). In
this document a certificate **taken away** is a certificate with such a record, whoever wrote
it.

**Why a kind of its own** (read from the product). Today `DELETE /gate/certificate` revokes the
certificate on the connection, `CA.md` §217, and the reference client calls it with the old
certificate after every renewal, once the new identity is stored, `PAIRING.md` §312. A renewal
copies the client key into the new record, so the old and the new certificate carry the same
client key. Written as a revocation that names the client's key, that call would refuse the
certificate the renewal had just issued.

**What the record holds** (S29). Its kind is `withdrawn` (S23, §658). It names the certificate
by its admission key, the SHA-256 over the certificate that `CA.md` §217 defines, in the member
`certificate`. In the member `renewed` it names the certificates that its copy holds as renewed
from that certificate when the record is written (below): the admission key of each renewed
certificate, each once, in ordinal order, separated by single spaces, and empty for none. In
the member `from` it holds the admission key of the certificate that the certificate taken away
was itself renewed from, where a record of kind `client` in force for it named such a
certificate, and it holds no such member otherwise. The subject in its identifier is
`certificate:` and the admission key of the certificate taken away, so that a deletion record
of it still tells which certificate was taken away (§609).

**Which renewals it names** (S29). `renewed` names every certificate that the copy holds as
renewed from the certificate taken away when the record is written. The copy holds a
certificate so in two ways. By an admission in force that reads and holds the certificate taken
away in `from`, whatever else refuses that admission: a revocation, an issuer retired or not
served, several admissions of its certificate, or a rule below. And by a statement in force
that took a certificate away and holds the certificate taken away in `from`: a record of this
kind present, or an item of this kind in a summary entry present. A deletion record holds no
origin, so what a deletion record alone tells of is named by nothing. It holds for every writer
of such a record, the operator's command and a client that gives its own certificate up alike.
Naming a renewal lets it count only as far as nothing else refuses it. It rules out naming only
the renewals that are admitted, which would turn on which authority is served at the write, and
naming only the renewals whose admission is still in force, which would turn on the order in
which the certificates of a chain were taken away (below). A record of kind `client` that does
not read is named by none (§615). **Where no admission in force of the certificate taken away
reads, its statement names nothing**: `renewed` is empty where the only records of kind
`client` in force for the certificate do not read. Such a certificate admitted nobody, so
taking it away ends nothing, and what was renewed from it was refused before the statement and
stays refused after it. It rules out naming those renewals, by which taking away a certificate
that admitted nobody would let a renewal onward from it count, with the chain behind that
certificate never asked. *Held by*
`AdmissionActsTests.A_certificate_taken_away_names_each_certificate_a_statement_took_away_after_it_was_renewed_from_it`,
by a record and by an item,
`AdmissionActsTests.A_certificate_taken_away_names_nothing_a_deletion_record_alone_tells_of`,
`AdmissionActsTests.A_certificate_whose_record_does_not_read_is_taken_away_with_the_origin_the_record_holds_and_naming_nothing`
and
`StoreAdmissionChainTests.A_renewal_onward_from_a_certificate_whose_record_does_not_read_stays_refused_once_that_certificate_is_taken_away`.

**Where the records of the certificate taken away hold other origins** (S29). A record holds a
single `from`. The origins of a certificate are the `from` of each admission in force that
reads and names it, an absent `from` among them, and the `from` text of each record of kind
`client` in force that names it in `certificate` and does not read, where that text is an
admission key. Where those hold other certificates of origin, the act writes a record for each
`from` among them, each with the same `renewed`, and the reading holds each of those records to
its own origin (rule 4). It rules out a single record with any of the origins, which would drop
a statement about the others, and it rules out passing over the origin of a record that does
not read, by which the chain behind that certificate would no longer be asked once its record
is deleted. *Held by* `StoreAdmissionsTests`, what each act composes, and `StoreWriteTests`,
what a write that takes such a certificate away holds.

**What it does in the reading** (proposed). A certificate is not admitted while a record of this
kind names it. Such a record counts as a revocation counts: as an entry wherever it sits, as an
item of any summary entry present, and as a deletion record that tells of it, whether or not a
deletion record names the entry. It is final. A client whose certificate was taken away and who
is to have access again renews with another certificate it still holds, or pairs again.

**A renewal made from a certificate that was taken away does not count** (proposed; its forms
are fixed, S29). Without that, taking a certificate away leaves a way round it (reasoning, not
run):
the certificate is taken away at a gate, and whoever still holds it, the client or somebody who
took it, renews it at another gate whose copy is from before. That gate's operator sees an
ordinary renewal and approves it. The new certificate carries the same client key, the record
names only the old certificate, and after the copies meet the new certificate is still
admitted. So:

1. **An admission that a renewal writes names the certificate it was renewed from**, in its
   member `from` (§615). An admission that a pairing writes names none.
2. **A record that takes a certificate away names every certificate that its copy holds as
   renewed from that certificate when the record is written**, as a record names in `after`
   what its writer had read (§610): in `renewed`, the certificate of each admission in force
   that holds it in `from`, and of each statement in force that took a certificate away and
   holds it in `from`. Those are the renewals made before the certificate was taken away, in
   the copy where it was taken away, the certificate the client has just renewed to among
   them, and a renewal that was itself taken away since.
3. **An admission renewed from a certificate that was taken away counts only where every
   statement that took that certificate away names it** (S29). A statement is a record of this
   kind present, an item of this kind in any summary entry present, or a deletion record that
   tells of such a record and is answered for by no record present and by no item (§623).
   Where several gates took the same certificate away apart, each having read other renewals,
   a renewal counts only where each of their records names it. A deletion record that stands
   for a record whose content is nowhere names none, so beside it no admission renewed from
   that certificate counts. An admission renewed from an admission that does not count for
   this reason does not count either, so a certificate renewed onward from it is refused as
   well.
4. **The chain is followed through the records that took certificates away** (S29). Whether an
   admission counts is asked of the certificate it was renewed from, and of the certificate
   that certificate was renewed from in turn. For a certificate whose admission is in force,
   that is the `from` of its admission. For a certificate that was taken away, it is the
   `from` that the records and the items which took it away hold, and where they hold several,
   each of them has to pass. **For a certificate that was taken away while an admission of it
   is still in force**, as in a copy that received the record by a merge and has not written
   since, it is both: each `from` the statements hold and the `from` of each such admission,
   each of which has to pass (S29). Asking both only refuses, and it rules out asking the
   admission alone, which would pass over a `from` a statement holds. What the admission adds
   is narrow: a statement the plugin composes holds the `from` of the admission its writer
   read, so the two differ only for a record the plugin did not write. That admission is
   deleted by the copy's next write (§627), and its `from` is asked until then. A certificate
   met a second time along its own chain does not count.
5. **An admission whose `from` names a certificate that is neither admitted by a record in
   force nor taken away does not count** (S29).

**Why the record that takes a certificate away holds `from`** (reasoning, not run). The
admission of a certificate taken away is deleted in the write that takes it away (below), and
with it the only statement of what that certificate was renewed from. Without `from` in the
record, the third rule could be walked round: a certificate that somebody took is taken away
at a gate, renewed at another gate whose copy is from before, renewed onward there, and the
certificate between is then given up at that gate. The record written there names the last
certificate, in good faith, and once the copies meet nothing would tell that the certificate
between was itself a renewal that does not count. With `from`, the reading follows the chain
back to the certificate first taken away, finds the certificate between unnamed there, and
refuses everything renewed from it.

**Why every statement has to name a renewal, and not any of them** (S29; reasoning, not run).
A record that names a renewal lets that renewal count, so among records made apart the reading
takes what all of them allow, as everywhere in the store (§610). Were a single record that
names it enough, the third rule could be walked round with a step more. A certificate that
somebody took is taken away at a gate. Its holder renews it at another gate whose copy is from
before, and then gives the old certificate up at that gate. The record written there names the
holder's renewal, in good faith, and once the copies meet that renewal would count by it. With
every statement asked, the record of the first gate does not name that renewal, and it is
refused. The store cannot tell that sequence from an owner who renews at a gate while the old
certificate is taken away at another gate that has not read the renewal, so it refuses both,
and that client pairs again. It rules out the renewals that any of the records names, which
admits whoever renewed last at a gate whose copy was behind.

**Why a statement names what was already taken away** (reasoning). What a client keeps must
not turn on which of two certificates of its chain was taken away first. Take a chain in which
a third certificate was renewed from a second, and the second from a first. Where the first is
taken away and then the second, the statement about the first names the second, whose admission
is in force then, and the statement about the second names the third: the third counts, since
every statement about its origin names it, and its origin passes at the first (rule 4). Where
the second is taken away and then the first, the admission of the second is deleted in the
write that takes it away (below), and what is left of the renewal is the statement about the
second, which holds the first in `from`. The statement about the first names the second by
that statement, so the third counts in this order as well. Naming admissions in force alone
would name nothing there, the second would not pass at the first, and the third would be
refused, in a copy that held nothing else than in the other order. **A statement vouches for
what its copy held**, in force or already taken away: it tells every later reading that the
renewals it names were made before the certificate was taken away, in the copy where it was
taken away. It vouches for nothing that had not reached that copy. A renewal made in another
copy from a certificate that was already taken away is named by no statement written before
it arrived, and stays refused, with whatever was renewed onward from it, also once the
certificate between was given up there. And a deletion record vouches for nothing, since it
holds no origin: a certificate that a deletion record alone tells of is named by no statement
about its origin, and what was renewed from it stays refused. *Held by*
`StoreAdmissionChainTests.Certificates_taken_away_in_either_order_leave_the_renewal_after_them_admitted`.

**Why an admission whose certificate of origin is nowhere does not count.** Every deletion of
an admission by the plugin comes with a record that covers what was renewed from it: a
revocation and a retirement refuse the renewal as well, and a certificate taken away names
it. So an admission whose certificate of origin has no admission in force and was not taken
away follows a deletion by hand, or the loss of the record that took that certificate away in
each of its carriers (§639). Counting it would admit a renewal that such a lost record did not
name. Its cost is the cost this rule already had: an admission is refused when the admission
it was renewed from is lost and nothing tells why, and that client pairs again.

This form is taken, and not the plain statement that taking a certificate away does not cover
its renewals, because it only refuses: no admission counts under it that would not count without
it (reasoning). **What it does not close**, stated plainly: a renewal that was made before the
certificate was taken away, in a copy the writer had read, stays admitted, since the record
names it. Somebody who took a certificate and renewed it before its owner gave it up holds a
certificate that works, and so does whatever was renewed onward from that renewal in the same
copy, also once the renewal between is taken away, since each statement names what its copy
held. **Taking a certificate away ends that certificate alone, and not what was already
renewed from it.** Only the operator stops that, by taking each such certificate away as well,
or by revoking the client, which refuses every certificate under the client's key at once
(§615) and is the act for a certificate that may have been taken and renewed from. The
certificate a client has just renewed to is never refused by the old certificate being given
up at a gate whose copy holds the renewal, since the record written there names it. What
refuses it is a further statement about the old certificate, made at a gate that had not read
the renewal, which is the sequence the third rule refuses. No answer turns on the order in
which entries are read, since what every statement names is the same in every order, nor on
the order in which the certificates of a chain were taken away in a copy, above. *Held by*
`StoreAdmissionChainTests`: a renewal that the record names is admitted and a renewal it does
not name is not; a renewal onward from a renewal that does not count is not admitted, also
once the certificate between was given up; certificates of a chain taken away in either order
leave the renewal after them admitted; a renewal from a certificate that only a deletion
record tells of is not admitted; an admission whose certificate of origin is nowhere is not
admitted; records for a certificate that hold different certificates of origin admit only
where each passes; where several records took a certificate away, a renewal that each of them
names is admitted and a renewal that any of them does not name is not, in every order of
entries; a deletion record of a record that an item restates changes nothing, and a deletion
record that neither a record nor an item answers for refuses every renewal from that
certificate; a certificate taken away while its admission is still in force is asked of the
`from` of both; a chain that leads back into itself admits nobody; and a chain of admissions
several times longer than the ceiling on items is answered without recursion. The refusal of
an admission that stays beside the statement taking its certificate away, by a record with its
item, by an item alone and by a deletion record alone, is held by
`StoreAdmissionsTests.A_certificate_taken_away_is_refused_while_its_admission_stays_beside_the_statement`.

**What restates it** (S29). Each record that takes a certificate away is restated by an item
of its own (§611), which holds the record's identifier in `id`, the certificate, and the
record's `renewed` and `from` as the record holds them. A record is restated only by an item
that holds its identifier and the same `certificate`, `renewed` and `from`, text for text, a
member that is absent being equal only to a member that is absent. So records that took the
same certificate away and differ in what they hold are each restated apart, and nothing a
writer stated is merged into another writer's statement. It rules out an item that restates by
the certificate alone: beside such an item, a record that names fewer renewals, or holds a
`from` that the item lacks, could be lost with nothing to show it, and what it refused would
count again. The identifier is what lets a deletion record of a record that an item restates
change nothing (§623). A write carries on every item of this kind that a summary entry present
holds, as it is and once; adds an item for each record present that none restates, the records
of its own act among them; and writes, for a deletion record that stands for a record whose
content is nowhere, an item with that deletion record's identifier as `id`, the certificate,
and an empty `renewed`. An item of this kind that holds no `id` is no item of its kind, and
the summary entry that holds it does not read as a summary entry (§658). A record of this kind
that no summary entry present restates stops the store as any decision without its summary
does (§624). *Held by* `StoreInForceTests`: records that took a certificate away and differ in
`renewed`, each with its item, leave the store whole, and a record whose item differs from it
in `renewed`, in `from` or in `id` stops it; and by `StoreWriteTests`: a write carries each
such item as it is, writes an item with an empty `renewed` for a deletion record that nothing
answers for, and none for a deletion record of a record that an item restates.

**Beside a revocation by key.** The two are independent. A certificate taken away is that
certificate refused and nothing more: the client's other certificates keep working, and a
standing answer about the client's renewals is untouched. A revocation refuses every certificate
under the client's key, a certificate taken away among them.

**Beside the admission and its expiry.** The admission of a certificate taken away is deleted
with a deletion record in the write that takes it away (proposed, as an admission beside a
revocation is deleted, §627), and admits nobody before that write lands or after it. Past the
certificate's end the gate refuses it on its dates, as today, and the record then refuses
nothing more. It stays all the same, with its item in every summary entry: nothing is removed
for its age (§643).

**The route.** `DELETE /gate/certificate` keeps what it means to a client: it takes no body and
names nothing, the certificate on the connection is what is given up, and a client can give up
its own certificate and nobody else's, `CA.md` §217. It writes this record through the single
writer, and answers once the database is saved and the file read back. The reference client
goes on calling it with the old certificate after a renewal, `PAIRING.md` §312. A client that
gives up the only certificate it holds has none to renew with, and pairs again as a new client
(reasoning).

**The clients page.** A client's certificates are a group of rows. The rows are grouped by
client key, and within a group the certificate admitted last is first. The first row of a group
is the client's own row: it carries the client's name and the X. Every further row of the group
is indented under it, reads `earlier certificate` where the client's own row reads the name,
and has no X. Profiles, Admitted and Until are each row's own, and a row is a certificate for
everything else: its tooltip names the client key and the certificate whole, a row whose renewal
stands refused is red, and **Allow renewal** acts on the row's certificate. **The X revokes the
client**, with every certificate it holds: it writes a revocation by the client's key (§615),
and every row of the group goes at once. **The menu of a row** names the certificate it acts on
in its first line, which is gray and no command: the first eight characters of the admission
key and the day the certificate was admitted. On the client's own row the menu offers
**Revoke**, which does what the X does, **Take certificate away**, which writes this record for
the row's certificate alone, and **Allow renewal**. On an indented row it offers the last two.
**Taking a certificate away takes its row.** Where that was the client's own row, the next row
of the group becomes the client's own row. Where it was the client's last certificate, the
client has no row and is not revoked. A certificate that several admissions name goes from each
row that names it. The page reads the certificate gone at once, and the act waits in the
dialog's buffer as a revocation does, so Cancel takes the removal back. **The buffer holds
which certificate is to be taken away, and the records are composed in the write at OK**,
against the store as it stands then with the acts pressed before it planned over it together,
as the single write OK makes of them (§627). So `renewed` names what the store holds at OK: a
renewal made from that certificate between the press and OK is named and keeps counting, and
of two certificates of a client taken away on the same dialog the second act sees the first,
so the client's newest certificate stays admitted in either order of the presses. The page
lists from the same composition, so no row reads `not counted` that OK will leave admitted.
**An act that has nothing left to end comes to nothing**: where a statement took the
certificate away by then, by its client or in a copy merged in, or where no record of kind
`client` in force names it under a client that the store does not take away. The second case
is a client that a revocation ended, and a record deleted by hand, also where the deletion is
merged in while the dialog is open. At the press such an act is refused with nothing held, so
a certificate pressed after its client's revocation on the same dialog is refused and OK
writes the revocation alone, while pressed before it, its statement is written and then the
revocation. At OK such an act writes nothing, and the log names the press and that nothing
was left to write. A record of the certificate that a later merge brings back is then admitted
again, as any record deleted by hand is (§639, row 11): no statement was written about it. A
record that does not read is taken away with the origin its own record names, and with the
origin that an admission of its certificate brings before OK, and its statement names nothing
(above), so the row is cleared and nobody is admitted by it. A press that is refused leaves
every row, and the line under the list reads "That certificate was not taken away: " and the
reason, which for an act with nothing left to end is "the act no longer applies to the store
as it stands". *Held by*
`ClientListMenuTests.Take_certificate_away_on_the_menu_takes_that_row_alone_and_the_next_becomes_the_client_s_own`,
`ClientListMenuTests.An_earlier_certificate_s_menu_offers_taking_it_away_and_no_revocation`,
`ClientListTests.Taking_a_client_s_last_certificate_away_leaves_no_row_and_revokes_nobody`,
`ClientListTests.Taking_away_a_certificate_listed_twice_takes_each_row_of_it`,
`ClientListTests.A_refused_taking_away_leaves_every_row`,
`AuthorityPanelTests.Taking_a_certificate_away_waits_for_OK_and_leaves_the_client_s_other_certificate`,
`AuthorityPanelTests.Taking_a_certificate_away_and_canceling_leaves_it_admitted`,
`AuthorityPanelTests.A_renewal_landing_between_the_press_and_OK_is_named_by_the_record_OK_writes`,
`AuthorityPanelTests.Two_certificates_of_a_client_taken_away_on_one_dialog_in_either_order_leave_its_newest_admitted`,
`AuthorityPanelTests.A_record_that_will_not_read_taken_away_is_written_with_the_origin_an_admission_of_its_certificate_brings_before_OK`,
`AuthorityPanelTests.A_record_that_will_not_read_taken_away_holds_the_origin_its_own_record_names_and_admits_nobody`,
`AuthorityPanelTests.A_certificate_taken_away_after_a_revocation_of_its_client_comes_to_nothing_and_before_it_lands_first`,
`AuthorityPanelTests.A_press_on_a_certificate_taken_away_since_the_page_was_drawn_is_refused_with_nothing_held`,
`AuthorityPanelTests.An_OK_at_which_some_certificates_were_taken_away_by_then_writes_the_others`,
`AuthorityPanelTests.The_page_reads_the_store_as_it_is_where_every_act_held_comes_to_nothing`,
`AuthorityPanelTests.A_certificate_taken_away_beside_a_revocation_and_a_command_on_the_authority_lands_with_them`
and, over a real database,
`RevocationInRealDatabaseTests.A_certificate_given_up_by_its_client_and_the_certificate_before_it_taken_away_on_the_tab_leave_the_newest_admitted`,
`RevocationInRealDatabaseTests.A_certificate_taken_away_on_the_tab_is_gone_from_the_file_after_OK_and_a_save`
and
`RevocationInRealDatabaseTests.A_certificate_taken_away_on_the_tab_that_is_never_saved_stays_admitted`.
The single removal is how a record is cleared that a lost answer to a renewal left behind,
`PAIRING.md` §312: the client never held that certificate and cannot give it up. **A row whose
record does not read** (§615) stands under its client where its `client` is a client key that
a group is listed for. Records that do not read whose client key reads, and whose client no
group is listed for, stand together: the first of them is that client's own row, and the
others are indented under it, in the order of the listing. A record whose `client` is no
client key stands on a row of its own. Such a row reads `not counted` where a row reads its
date, with the reason in its tooltip, and it offers each command for which the record holds a
readable member: Revoke where `client` is a client key and the row is the first of that
client, Take certificate away where `certificate` is an admission key, and no command
otherwise
(`ClientListTests.A_record_that_will_not_read_is_taken_away_by_the_certificate_it_names_and_otherwise_refuses`,
`ClientListMenuTests.The_menu_of_a_record_that_does_not_read_offers_what_the_record_names`).
**The X carries what it does as the tooltip of its own cell**, and a row without an X shows no
tooltip in that column. **Where the store takes no write in the session** (§636), no row
carries the X and a row's menu offers neither Revoke nor Take certificate away
(`ClientListMenuTests.Where_the_store_takes_no_write_the_menu_offers_Allow_renewal_alone`). A
certificate that several admissions name (S24) reads `listed twice` on each of its rows. **A
renewal that does not count reads `not counted` where its date would be**, as a record that does
not read does, and its tooltip gives the reason: "Renewed, directly or further back, from a
certificate that was taken away without naming the renewal made from it, or that no single
admission admits, so it admits nobody." (rules 3 to 5). The row keeps its place in its client's
group, with the commands of any row. **Whatever counts or names clients takes only admissions
that admit**, by the same listing: the count that a Recreate or a Delete is confirmed against,
the warnings about certificates near their end, and the clients the dialog that asks which
authority to keep lists for each authority, which are the admissions that would count were that
authority kept. A certificate listed twice, an admission under another authority and a renewal
that does not count are no client to any of them. *Held by*
`ClientListTests.A_renewal_that_does_not_count_reads_not_counted_with_why_in_its_tooltip`,
`StoreAdmissionChainTests.The_page_reads_a_renewal_whose_chain_fails_as_the_gate_does`,
`AuthorityPanelTests.A_renewal_that_does_not_count_and_a_certificate_listed_twice_cost_nothing_to_recreate`,
`AuthorityDescriptionTests.An_admission_that_admits_nobody_is_not_warned_about` and
`AuthorityChooserTests.A_renewal_that_would_not_count_were_its_issuer_kept_is_no_client_of_it`.
**A row reads `not this authority` where its date would be** in two cases: a certificate that
the authority served did not issue, by the anchor pin, and an admission whose `issuer` is not
the authority served, by the judgment of the listing (§615). The row takes what an admission
comes to from the listing, as the gate does, so the page shows no date for an admission whose
certificate chains to the authority served while its `issuer` names another. Like everything on
the tab, each command waits for the settings dialog's OK. No column is added for any of this,
and nothing on the tab scrolls. **The Client column is held at least as wide as the fixed text of
an indented row**, `earlier certificate` or the text of a record that does not read under its
client, at every scaling: a client's name on its own row may be cut, since its tooltip holds it
whole, and a fixed text has no other place to be read. *Held by* `ClientListTests`, among them
`An_admission_naming_an_authority_that_is_not_served_says_so_where_a_date_would_be`,
`Records_that_do_not_read_of_a_client_no_group_lists_stand_together_under_the_first_of_them`,
`A_certificate_listed_twice_and_a_record_that_does_not_read_show_why_in_their_own_words` and
`The_X_shows_what_it_does_on_its_own_cell_and_a_row_without_an_X_shows_nothing_there`, and
`GateTabFitsTests.Clients_with_standing_answers_fit_the_page_as_every_other_client_does`, which
measures a group of rows at its longest texts.

**The audit.** As today, `DESIGN.md` §120 and `CA.md` §217: a line by the admission key that was
taken away and the client key its record carried, for the client's own act written by the writer
on its landed path, and for the operator's when the dialog's OK lands it. **A certificate that
the operator took away** is written as a single line, and only where the OK wrote the record.
The line names the admission key and the client key of the admission that admitted the
certificate before OK, also where the certificate was past its dates and the gate refused it
for them: the line tells whose certificate ended. A certificate that admitted nobody before
OK, listed twice, under an authority that is not served, a renewal that does not count or a
record that does not read, gets the line with the admission key and no client key, and so does
every certificate where the store cannot be read before OK is applied: the operator's act is
on record, and no client is named as having held what admitted nobody. A certificate whose
client the same OK revokes has the revocation's line and no second line. A certificate that
was given up or taken away before OK, that was pressed after its client's revocation, or whose
records were deleted by hand, a deletion merged in under the dialog among them, gets no line,
since the OK wrote nothing for it; the log names the press. The line reads as a revocation's
does, and it is written where a revocation's lines are, below. **The operator's
revocation of a client names no certificate**, so it is written as a line for each certificate
of that client that the OK ended in this copy, each with its admission key and the client key,
and as a single line with the client key alone where the copy admitted no certificate of the
client. A certificate the OK ended is a certificate this copy admitted before it: an admission
under an authority that is not served, a certificate that several admissions name and a record
that does not read admitted nobody, and get no line. Which certificates those are is read from
the store before OK is applied, since the write that revokes deletes the admissions it refuses;
where the store cannot be read then, the single line with the client key alone is written.
**The lines are written once the store's write is taken**, and the database is marked before
them, so a revocation the store holds has its lines, and what the lines threw reaches the
catch-all of the dialog's close once the buffer is let go, so a defect there loses nothing else
of the OK, a name and a lift written in the same write among it
(`GateWiringTests.A_defect_writing_a_revocation_s_lines_loses_nothing_else_of_the_OK`). **The
log tells what the write came to before anything that follows it in the tab's OK**: once the
outcome of the store's write is known, the store's part logs a line of its own that names
each act written, a client revoked, a certificate taken away or a lift by its short key, or a
warning that names what was not written and why. Then the lines of the audit are written, and
the buffer is let go last, with the store key each act holds overwritten, whatever the lines
throw (§627). So the log holds what landed also
where a line of the audit throws. **That holds from the write's outcome on, and not inside the
write**: the writer redraws KeePass's main window once the write has changed the entries,
before the tab learns the outcome, and nothing on the writer's path logs an operator's write
before that redraw. A defect thrown out of the redraw therefore leaves entries written with no
line telling what landed and no line of the audit: the buffer is let go,
and the dialog's close records the incident with what was thrown. A failure of the redraw
that the writer names as the user interface's is caught there and logged, and loses nothing.
What the tab does after the store's part runs the same way, whatever the lines came to: the
lines of the lifts that landed, the answers lifted for this session, and the
invitations made on the dialog kept, so a defect in the lines leaves no answer standing that
the operator lifted
(`GateWiringTests.A_defect_writing_a_revocation_s_lines_keeps_no_answer_lifted_for_the_session_standing`).
*Held by* `PendingRecordsTests.An_OK_whose_lines_throw_after_the_write_landed_lets_the_buffer_go_last`,
`GateWiringTests.A_revocation_writes_a_line_for_each_certificate_of_the_client_and_the_client_key_alone_where_the_copy_holds_none`,
`GateWiringTests.A_revocation_writes_lines_only_for_the_certificates_this_copy_admitted`,

`GateWiringTests.A_revocation_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel`,
`GateWiringTests.A_certificate_taken_away_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel`,
`GateWiringTests.A_certificate_taken_away_and_its_client_revoked_on_one_OK_is_written_down_once`,
`GateWiringTests.A_certificate_that_admitted_nobody_before_OK_is_written_down_once_with_no_client`,
`GateWiringTests.A_certificate_taken_away_with_the_store_unreadable_before_OK_is_written_down_with_no_client`,
`GateWiringTests.A_certificate_its_client_gave_up_before_OK_writes_nothing_at_OK_and_no_line`
and `GateWiringTests.A_defect_writing_a_taken_certificate_s_line_loses_nothing_else_of_the_OK`.

**What it has to satisfy.** A certificate its client gives up is refused from the next request.
The certificate a renewal issued keeps working. A lost answer to a renewal costs the database
nothing, `PAIRING.md` §312. Whatever takes a certificate away is restated by the summary entry.
A revocation by the operator still refuses a certificate renewed at another gate.

**Run over copies that are merged.** Two copies of a real database, written apart by the
product's own acts and saved, are merged each into the other by `PwDatabase.MergeIn`, from the
other copy's file opened afresh, under Synchronize, Overwrite if newer, Overwrite existing, Keep
existing and Create new IDs. The copy merged into is asked which certificates it admits, of
the gate's own target for the merged database, and asked again after its next write of the
plugin's own, a pairing; where a sequence leaves a store stopped, the state is held as well.
The operator's single removal is made through the tab's buffer, the press and the OK, and a
client's giving up through the writer, which over a single database leaves a client its newest
certificate where it gives up the certificate between and then the certificate before it
(`GiveUpInRealDatabaseTests.A_client_that_gives_up_the_certificate_between_and_then_the_one_before_it_keeps_its_newest`).
Some sequences take a certificate away in the copy merged into once the copies have met. What
the runs came to, in both directions and under each method but Create new IDs:

- A renewal and the old certificate given up in a copy, beside a copy from before: the old
  certificate is refused and the renewal admitted.
- A renewal made meanwhile in the other copy, and a renewal onward from it, also once the
  certificate between was given up there: both are refused, and the first copy's renewal is
  admitted.
- The same certificate taken away in both copies, each having read another renewal: both
  renewals are refused.
- The operator's single removal in a copy and a renewal in the other: the renewal is refused.
- The operator's single removal in a copy, and in the other a renewal, a renewal onward from
  it, and the renewal between given up: the record written in the other copy names what was
  renewed onward, the first copy's record names none of it, and everything renewed in the
  other copy is refused in both
  (`A_renewal_given_up_in_a_copy_after_the_other_took_its_origin_away_refuses_what_was_renewed_onward_from_it`).
  The same holds where the holder there gives the first certificate up as well, after the
  renewal between
  (`The_operator_s_statement_and_the_holder_s_give_ups_in_the_other_copy_refuse_every_certificate_renewed_from_it`).
- A renewal, a renewal onward from it and the renewal between given up, all in a copy, and the
  first certificate taken away in the copy merged into once the copies have met: the statement
  written then names the renewal between by the statement that arrived, and the renewal
  onward counts
  (`A_certificate_taken_away_after_a_statement_of_the_other_copy_arrived_names_what_that_statement_was_renewed_from_it`).
- A renewal taken away in a copy before the other copy renewed from it, renewed onward and
  gave the renewal between up, and the first certificate taken away in the copy merged into
  once the copies have met: the statement written then names the renewal the first copy took
  away, whose statement names nothing of the other copy's, and everything renewed in the
  other copy stays refused
  (`A_statement_written_after_a_merge_about_an_origin_still_admitted_leaves_what_the_other_copy_renewed_refused`).
- The record deleted for good with its summary entry kept: the item still refuses the
  certificate and names the renewal the record named, and the other copy's renewal is refused.
- The record edited by hand to name the other copy's renewal: within the history limit the
  record counts by the version written and that renewal stays refused; past the limit the
  store stops wherever the edited entry arrives (§622).
- The record that both copies hold, the copy made after the certificate was taken away, then
  edited by hand in a copy: within the history limit it counts by the version written wherever
  the edited entry arrives. Past the limit, a store left with no version of the record that
  passes stops, and a store that keeps the version written, as its current version or in its
  history, stays whole and counts by it; which merge leaves which, for edits a second apart
  and for edits within the second the record was written, is in §622
  (`A_record_both_copies_hold_edited_in_a_copy_counts_by_the_version_written_or_stops_the_store_past_the_limit`).
- The record that both copies hold, deleted for good in a copy: the certificate stays refused
  in both, and the renewal the record named stays admitted
  (`A_record_both_copies_hold_deleted_for_good_in_a_copy_still_refuses_in_both`).
- A revocation of the client beside a certificate taken away: every certificate of the client
  is refused.

Under Create new IDs the copy imported into answers as it did before, in every sequence (§621,
§640). After the next write of the copy merged into, every answer is the same. No sequence
ended with a certificate admitted that a statement in the merged file refuses. **Two cases
leave certificates admitted, and §639 has both**: the record and its summary entry both deleted
for good where they were made, merged under a method that carries no deletion record (row 2),
and an admission of origin deleted by hand, merged the same way (row 11). In the first no
statement about the certificate is left in the file merged into, and in the second the
certificate of origin is still admitted there, so in neither is a rule of this section passed
over. A gate that has not taken the file in still admits a certificate taken away at another
gate, as it admits a client revoked there (§639). KeePassXC, a second KeePass with the plugin
and a device were not run (§644 item 16). *Held by* `StoreAdmissionMergeTests`.

**What is built.** The record, what composes it, and what it does to the certificate it names.
`AdmissionActs.TakeAway` composes the records of the act as this section has them: a record for
each certificate of origin that the records of kind `client` in force for the certificate
hold, read or not, an absent origin among them, each naming in `renewed` every admission in
force that reads and was renewed from the certificate, and every certificate that a statement
in force took away and holds as renewed from it, and naming nothing where no admission in
force of the certificate reads. `DELETE /gate/certificate` writes them for the certificate on
the connection, by a write that is saved before it is answered, and the plan of that write
deletes the certificate's admission and restates each record by an item of its own (§627). A
certificate that a record, an item or a deletion record states as taken away is admitted by
nobody, and its admission is counted among no admissions of a certificate (§615). A certificate
that is no longer admitted when the write is made is answered `401 unpaired`, and nothing is
written. The clients page is built: the rows grouped by client, the X and **Revoke** on the
client's own row, **Take certificate away** on every row that names a certificate, with what
follows from a row going alone, the menu's first line, and the rows marked `not counted` and
`listed twice`, each with Revoke where it is the first row of a client it names. The audit's
lines are built for a certificate that its client gave up, for a certificate that the operator
took away and for a client that the operator revoked. **The rules about a renewal are built**,
rules 3 to 5 with the chain of rule 4. Whether an admission counts is asked along its chain of
origins by iteration, each certificate worked out once for the question or the listing asked, so
a long chain costs no depth of calls and a chain that leads back into itself ends: `StoreInForce`
holds, for each certificate, the statements that took it away with their `renewed` and `from`,
and the admissions in force that name it, worked out as the reading is made. A chain as long as the
store can hold statements held KeePass's thread for 27.2 ms at the median (§620). The gate's
hold, an approval held for a renewal at its write, and a certificate given up all ask the same
judgment, so a renewal that does not count is answered `401 unpaired` wherever it asks, and the
listing marks it for the clients page. **The runs over copies merged by KeePass are made**,
above; what §644 item 16 still owes is the same in KeePassXC, between two KeePass instances and
on a device. *Held by* `AdmissionActsTests`,
`StoreAdmissionsTests`, `StoreAdmissionChainTests`, `RevocationOperationTests`,
`ClientListTests`, `ClientListMenuTests` and `AuthorityPanelTests`, and over real databases by
`GiveUpInRealDatabaseTests`, the record in the file, the certificate a renewal issued still
admitted once the old certificate is given up, and a write that no save kept taken back out
with what it deleted put back, and by `StoreAdmissionMergeTests`.

### Profile names §616

**An addition** is a record of kind `profile` with the members `name` and `after`: the
identifiers of the removals of that name that the writer had read. **A removal** is a record of
kind `removed` with the member `name`.

**A name is on the list** when some addition of it, not beside a deletion record naming it, names
in `after` every removal of that name the file knows. A removal is known as an entry, as an item
of any summary entry present, or as a deletion record that tells of it. So a removal wins over
every addition that had not seen it, and a name added again after a removal is a new addition
that names that removal.

**What follows for two copies.** A name added in each of two copies stays, since neither addition
had a removal to see. A name added in a copy and removed in another is off the list once the
copies meet, and the operator adds it again (evaluation 6, row 23).

**What replaces what.** An addition that does not name every known removal of its name is deleted
with a deletion record at the plugin's next write (§627). A removal is never deleted, as
measured; the design paper deleted a removal once an addition in force named it. What a name
means is unchanged: a name the list does not hold grants nothing, whatever rule still names it,
`DESIGN.md` §139.

**Which records count** (this document's own). A record of kind `profile` is an addition where it
is in force, reads as its kind, and its `name` is a name the issuer could issue as a claim,
`AuthorityIssuer.IsUsableProfileName`, the single rule of a name in `DESIGN.md` §139. Names are
compared exactly, by ordinal. A record whose `name` is no such name is no addition anywhere: it
puts nothing on the list and stops nothing, since §658 has the core hold `name` as a text it does
not look into. `after` names each identifier once, in ordinal order, separated by single spaces,
and is empty where the writer read no removal of the name; it is read as a set. **The removals an
addition has to name** are every pair of a name and an identifier that the reading holds: a
record of kind `removed` by its identifier, an item `removed` by its `id`, and a deletion record
that tells of a removal by its own identifier (§623 step 2), a removal known in several of those
ways counted once. A removal whose `name` is no name an addition can carry is a removal of nothing
on the list, and an item whose `id` is no identifier can be named by no addition, so its name
stays off the list. It rules out a second rule for a name beside the issuer's, a comparison
without case, and a store stopped for a name out of form.

**What the plugin writes** (this document's own, as the measured code writes an addition and a
removal).
An addition names every removal of its name that the reading it is composed over holds, and every
removal that a deletion record of that reading tells of once the name counts as known, and is
composed only where the name is not on the list. A deletion record tells of a removal only of a
name the file knows (§623 step 2), and the addition is what makes the name known: where the
removal's record and every item restating it are gone, as a Synchronize with a copy that deleted
them leaves it, the reading the addition is composed over holds no removal of the name, and the
file the addition is written into tells of the removal by its deletion record. It rules out an
addition naming only the removals that reading holds, which its own write would put out of force
and the plugin's next write would delete, so that the name would stay off the list until it was
added again. A removal holds `name` alone, and is composed only where the name is on the list, so
no removal is written for a name already off it, which would add an item for good with nothing to
take away. **A pairing's additions are records of the admission's own act**, after its record of
kind `client`, so that the names and the admission are planned, saved and taken back together,
and left out together where the admission's issuer does not stand alone (§627): an addition for
each approved name the list does not hold in the reading of the write's turn, naming the removals
of its name in that reading as any addition does. Each of those removals is a removal the approval
dialog's last reading held, since a removal beyond them makes the approval stale and writes nothing
(`PAIRING.md` §311), as an Add
that a removal overtook writes nothing (§627).
**A name is added only where a store key stands** (§627 step 1): where none stands, the Profiles
page refuses the press and states that the database has no certificate authority yet, and with a
Create held by the same dialog the act is composed under the key that Create brings and written
in the same write, after it.

**The list keeps no ceiling** (S21). No write of the plugin's own refuses a name for the count of
names the list holds, and the reading cuts nothing short. What stays is a rule of a request and
not of the list: a pairing asks for 32 names at most, which is what the wire takes.

**Where a name is read** (this document's own). The gate reads a client's names in the hold that
reads its admission, from the same reading, and a reading that serves no authority gives the gate
no names: a hold of its own for the names would hold KeePass's thread again at every request. A
held approval is compared at its write, name by name, with the names on the list in the reading of
the write's turn, and for each approved name off the list with the removals of it in that reading,
each of which the dialog has to have held, `PAIRING.md` §311. The dialog's reading is a reading
made afresh on KeePass's thread with the store key, as the writer's turn reads, in the turn that
puts the dialog up and on each tick while it stands, and let go before anything leaves the turn:
the names on the list, the clients that regain a name off it and the removals of such a name
counted as the write counts them, `AskedProfiles`, a removal that only a deletion record tells of,
of a name the file does not know otherwise, among them. So the first approval of such a name puts
the name back, and the write finds a removal the dialog did not hold only where it landed after
the dialog last read the store.
The log names an act about a name by the name at the settings dialog's OK, and the audit holds no
line for a name added or removed, `DESIGN.md` §120. The grant editor's pick list is read from the
store at each redraw of its tab, and is empty where the store names nothing or cannot be read, so
it never offers a name removed since. The Profiles page offers no Add and no removal where nothing
is written into the store in the session, as the clients page offers no revocation, and the first
page of the tab states why (§636); nor where the store cannot be judged, under several keys or in
a form the plugin does not read, where the clients page lists nobody and so offers nothing.

**What is built.** The records, their reading and their writes as this section has them:
`ProfileNames` reads the names from a reading of the store, `ProfileActs` composes an addition and
a removal and composes each again over the store as it stands at OK, and a pairing's act carries
its additions after the admission. The gate reads a client's names with its admission, a held
approval is compared name by name in the write's turn, the Profiles page writes each name through
the store, refuses a name where no store key stands and offers no Add and no removal where
nothing is written into the store or the store cannot be judged (§636), and the grant editor's
pick list is read from the store. An Add that a removal of its name overtook before OK is not
written: the page's line states it while the dialog is open, the log names the name at OK, and
the name comes back by being added on the page again (§627). An addition, the page's and a
pairing's alike, names a removal of its name that only a deletion record tells of, and a removal
of that kind that arrives after the press overtakes an Add as any other does. The names over
copies that are synchronized are run over real databases, with what of that run stays owed in
§644 item 32.
*Held by* `ProfileNamesTests`,
against the store's meanings of the measured code (`tests/Shared/StoreMeaningVectors.json`), and
by every rule of this section over the product's reading, a hundred names among them;
`ProfileActsTests`, against its records (`StoreRecordVectors.json`); `AdmissionActsTests`;
`PendingRecordsTests` for the buffer, its fold and the additions it leaves out; `ProfileListTests`
and `DatabaseProfilesTests` for the page; `GateTabFitsTests` for the page with a hundred
names, and with the longest of each sentence its line holds, the refusal where no store key
stands among them; and `StoreProfileMergeTests` for names added and removed in copies of a real
database, each merged into the other under each of KeePass's methods, and for a name added back
where a copy kept nothing of its removal but a deletion record.

### Rules §617

**A rule is stored as a record for each node and profile** (a later decision). Each row of the
grant editor is a record of its own with its own versions: a record of kind `version` whose
object is a node and a profile. The node's switch "Refuse any profile not named here", which was
`default` in the rule document of release 0.2.0, is a record of its own with its own versions,
whose object is the node. Nothing is on the node.

**This replaces the form that was measured first.** Every run of the store before that
decision wrote a version of a node's whole rule: a single object for a node, `rule:` and the
node's identifier, whose version held a document for every profile. That is the design paper's
form as well. A later run wrote rules "per profile" (`measured-copies-extended` 1), and its
form is not this form: it was still a single version of a node's whole rule, written so that
it names each profile with its words.

**A record for each node and profile was run after it was decided**
(`measured-rules-per-profile` 1): a row's version under an object for the node and the
profile, the switch's under an object for the node, and nothing else of the store changed. In
the merge cells, the deletions, the edits and the random histories of the store as decided, no
cell and no file ended wider in that form that did not end wider in the whole-rule form
(`measured-rules-per-profile` 3). The parts that run had cut were run since, with each of four
KeePassXC releases (`measured-rules-cut-parts` 2, 3, 6): no cell differs between the releases,
none ends wider with a record for each profile where the whole-rule form did not, and every
change of class between the forms goes from wider to refusing. The hand deletions of those
parts take a single row's version, so they measure the loss of a single row, and the loss of
every row of a node at once was not run. §644 has what was run and what stays owed.

**What a version holds** (the forms are below, S8). Beside `object` and `after` (§610), and
`lost` where it settles a lost version (below), a version of a profile's rule holds a single
thing of three:

- **the letters granted**, out of the eight of `CRUSDLAP`;
- **no letter**, which is an empty rule: the profile may do nothing from there down, as an empty
  array was in the rule document, `DESIGN.md` §136;
- **the rule taken off**, which is the profile not named at that node. It is told apart from an
  empty rule. With it the version holds what arrived at the node for the profile when the rule
  was taken off, or that the node's own switch refused then (a later decision, below).

A version of the switch holds whether the node refuses a profile it does not name or lets the
walk go on, which was `deny` or `inherit` in the rule document, and where it lets the walk go
on, what arrived at the node for each profile when it was written (the same decision).

**The object and the text of a version** (S8; the shapes the runs wrote, with the product's
letters). A row's object is `rule:`, the node's identifier as 32 lowercase hexadecimal
characters, `/` and the profile's name. The switch's object is `switch:` and the node's
identifier in the same form. The name is read as the list reads a name (§616): a name the issuer
could issue as a claim, compared by ordinal, so `default` stays no name of a profile, as it is
for the issuer, the wire and the client. The text of a version is its `doc`:

| Version | Text |
| --- | --- |
| A row that grants | `row:` and each letter of `CRUSDLAP` that the rule grants, in that order and each at most once. `row:` alone is an empty rule |
| A rule taken off while the node's switch lets the walk go on | `off:` and the letters that arrived for the profile, written as a row's letters are. `off:` alone: nothing arrived |
| A rule taken off while the node's own switch refuses | `off:deny`, the whole text. No letters read so, since the letters are capitals |
| The switch refusing | `sw:deny` |
| The switch letting the walk go on | `sw:inherit`, and for each profile that at least a letter arrived for: a single space, the name, `=` and the letters that arrived. The names ascend by ordinal comparison, so each stands once. `sw:inherit` alone: nothing arrived for any profile |

The letters that arrived are a whole mask, the verbs and the switches of a rule as they arrived
together, and a mask of switches alone is written as it is and grants nothing. A switch's
version names every profile for which the rules give at least a letter at the node's parent,
whether or not the name is on the list and whether or not the node holds a row for it: the
letters are about rules, which a name off the list still has, `DESIGN.md` §139. A name is a name
the issuer could issue, so it holds neither a space nor `=`. **No member, no kind and no item
is added for what arrived**: it is in `doc`, which the item of the summary entry restates
whole, and the format number stays as it is (§658). The runs wrote these shapes with letters of
their own, in sorted order, which fix nothing of the product's letters
(`measured-rules-per-profile` 1, `measured-rules-across-levels` 1); the order of the letters,
the order of the names and which profiles a switch's version names where a name is off the list
are this document's own, and in no run. It rules out the words of the rule document of release
0.2.0 as the text, and a second rule for a name beside the issuer's.

**Each is read strictly, and a text out of form refuses** (this document's own; in no run, the
probe's reader raising on such a text). A standing version of a row whose `doc` is none of those
texts refuses its profile at the node, as a lost version does (below), whatever else stands for that
row, and a standing version of the switch whose `doc` is neither of its texts reads as refusing. Out
of form are `off:` followed by anything that is neither letters in form nor `deny`, and in a
switch's text an item with no letter, a name the issuer could not issue, a name repeated or out of
order, a second space, a letter repeated or out of order, and anything after the last item. It rules
out a text read generously, and passing such a version over, which would answer with what the
version may have been taking away, `DESIGN.md` §136. An object of type `rule` or `switch` in any
other text than its form above is the rule of no node (§658): no rule is read from it and it stops
nothing, as a name out of form is no name on the list (§616). **So a rule that will not read is no
state of a node.** A record of a rule that fails its check is damage, which stops the store (§621,
§624), where a rule document that would not read refused at its node alone, and `DESIGN.md` §136
records that reversal. *Held by* `RuleTextTests`, `StoredRulesTests` and `StoreReadingTests`.

**The three states of a row stay apart** (measured, `measured-rules-per-profile` 6). An empty
rule, a rule taken off and no rule ever made were each made at a node under a root that grants
and a switch that inherits, beside a copy that held an earlier row. An empty rule read as an
empty rule, and a rule taken off as a rule taken off, in 17 of 18 merge cells each, where the
eighteenth is the import with Create new IDs into the copy that stays stale (§640), and no
rule ever made read as that in 18 of 18. An empty rule and a rule taken off each read as
itself in 16 of 16 cells after the version was edited in the entry dialog, deleted for good
with and without a deletion record, and deleted to the recycle bin, each followed by
Synchronize and by a KeePassXC merge, both ways. After eleven edits of the version the store
refused, in 4 of 4 cells for each, which is the stop of §622 and no reading of another state.
A rule taken off never read as an empty rule after a merge, and an empty rule never read as a
rule taken off. Made apart, from a row both copies held, the two both stand, and the profile gets
nothing at the node, which is what both allow, in 16 of 18 cells; the other two are imports.

**The single exception is a loss, and it ends narrower.** Where a version that takes a rule
off is deleted for good with every summary entry that restates it, and a deletion record
tells of the version, the row refuses, and sealing the store again writes that refusal as an
empty rule (§625), in 4 of 4 cells. The profile then gets nothing where the rule taken off let
through what arrives from above. That is what the press writes for any lost version. The
reverse did not occur: after the same loss and the same press an empty rule read as an empty
rule, in 4 of 4 cells.

**The rule model of release 0.2.0 is unchanged** (decision 9): the nearest rule wins for each
profile, profiles are combined with or, a rule can be empty, the switch answers where no rule names
a profile, and the verbs and the two switches of a rule are `CRUSDLAP`, `DESIGN.md` §136 and §137.
What changes is where a rule is kept, and not what it means. The reading puts together, for a node,
the rules of the profiles named there and the switch, and hands the resolver what the rule document
handed it (reasoning), wherever each row and the switch stand in a single version. Versions that
stand apart are handed over as they stand, and what the resolver does with them is below (S9).
**A later decision changes the meaning in a single place**: a node that lets a profile through
gives what arrives from above only as far as it had arrived when the node was made to let it
through (below). This paragraph had the meaning as unchanged throughout.

**A profile's rule at a node is the versions of its object that nothing names.** A single
standing version is the rule. A newer version names every version of that object that its
writer had read, whatever it holds: that is the operator changing a row, and it may widen. No
standing version and no trace of any is a profile that the node does not name, which the switch
answers, as in that model.

**Two copies that change different profiles at the same node do not meet.** Each of the two
objects has a single standing version after the copies come together, both changes hold, and no
row is left to settle. In the whole-rule form the same two changes left two versions of the
node's rule, and every profile got what both allowed until the operator settled it. Measured
(`measured-rules-per-profile` 4), in seven cases of two profiles changed apart at a node, a
rule narrowed, widened, taken off, emptied, a row made where none was, and a row removed under
a switch that refuses: every merge both ways, by every KeePassLib method and every KeePassXC
release run, ended as decided in both copies with no row left to settle, in 112 of 112 cells
that are not imports. The 14 import cells end as the importing copy's own changes, which is
the stale copy of §640. The whole-rule form, run in six of the cases, left the node's rule in
two versions in all 96 of its cells and ended narrower in 80.

**Versions of the same profile's rule made apart give what all of them allow**, until the
operator has removed versions in the grant editor and a single version is left (§657;
evaluation 6, row 22). Several gates are supported, so more than two versions can stand, and
they are combined in any order with the same result (§610). A version that grants answers with
its own letters whatever arrives from above. A version that takes the rule off answers with what
the node gives a profile it does not name: nothing where the switch refuses, and what arrives
from above where the switch inherits. Versions that grant give, together, the letters all of
them hold, and a switch of the rule counts as a letter. A version that grants and a version that
takes the rule off give, where the switch inherits, what arrives from above cut to the granted
letters, and nothing where the switch refuses (the design paper 4.3, read for a single profile).
So a rule narrowed in a copy and taken off in another gives what the group above gives, and no
more than the narrowed rule. That combination was measured in the whole-rule form, with two
verbs and at most two profiles (`measured-store-as-a-whole` 2.4, 3.1), and for a single
profile's rule (`measured-rules-per-profile` 5). A profile set to read in a copy and to read
and write in another, apart, left two versions standing in 16 of 18 cells, and the node gave
the profile read; the other two cells are imports, which hold the importing copy's own
version. With a third copy that took the rule off, apart, three versions stood in 8 of 9
cells, and the node gave the profile read: what arrives from above, cut to the letters both
granting versions hold.

**A node that lets a profile through gives what arrives, cut to what had arrived when it was
made to let through** (a later decision, which settles S34). For a profile, a node either
answers by itself or lets through. It **answers by itself** where every standing version of the
profile's row grants, where the row refuses, or where the switch refuses and the row does not
grant: what stands above is not asked. It **lets through** where the row has no standing
version, or holds a version that takes the rule off among its standing versions, and the switch
lets the walk go on. **What arrives** at a node for a profile is what the node's parent gives
that profile, and nothing above the root. Only a version that makes a node let through can come
to give more than its writer saw. The writer of a row that grants saw what the row gives at
that node, whatever comes to stand above. The writer of a rule taken off, or of a switch turned
to let the walk go on, saw what arrived on that day, and the version goes on letting through
whatever arrives later. So such a version records what arrived, a rule taken off while the
node's own switch refused records that it lets nothing through, and the reading cuts what
arrives now to what the versions recorded. Nothing is refused for a record: the node gives what
arrives, less the letters that its own versions never saw. A node never ruled and a switch
never set hold no version and cut nothing, which is that model's meaning of a node without a rule.
The other side of the sequence cannot carry the record: the version above would have to name
every node below that lets through, which has no bound, where the version below states a single
thing about a single place.

**The cut is in capabilities, and not in letters taken singly.** A capability is a verb alone,
or a verb that reads or writes with a switch of the same rule. A version records a whole mask,
what arrived for the profile at a single moment in a single copy. The reading only ever
intersects masks: what arrives, the mask of each version that lets through, and the granted
letters. A write above (below) replaces a recorded mask by a whole mask. No step unites letters
of two copies, which is the only way a verb comes to stand beside a switch that no copy gave
it, and which a check by letters alone does not see (`measured-rules-owed-forms` 3).

**What that closes, measured** (`measured-rules-across-levels` 3, 4 and 7, where this form is
form B). In the model, in the configurations in which the reading without it drew 67, 33, 129,
44 and 44 histories of 200,000 with a merge that gave what neither copy gave, with two and four
profiles, two letters and the whole mask, two levels and three: none, by letters and by
capabilities. On files, the sequences that ended so in 16 of 16 cells each, across two nodes,
through a group between them and through a group's switch (§639), ended so in none, every merge
both ways by every KeePassLib method and every KeePassXC release run, and every other cell of
those parts ended as it did without the form. Every pair of a row's versions, with each mask
granted, each mask recorded and `off:deny`, under every mask arriving and either switch, gave
exactly what both versions allow, by capabilities, in 67,502,592 of 67,502,592 combinations,
and 200,000 random triples what all three allow. **The guarantee is of a merge** (reasoning,
with those runs): where two copies part from a state both hold, change rules and switches and
are merged, no node gives at that merge a verb, or a verb with a switch, that neither copy
gave, but in the classes that §639 keeps. An act above made afterwards, in a copy that holds
both changes, carries the nodes below that follow it. What the form costs is in §642.

**The resolver carries a limit up its walk** (S9; as the measured reading and the model put a
node's rule together, `measured-rules-per-profile` 1 and 5, `measured-rules-across-levels` 1).
The walk for a profile starts with a limit of every letter. At each node, from the node asked
about upward:

- **the row refuses**, for a lost version or a text out of form: the profile gets nothing, and
  the walk ends;
- **every standing version of the row grants**, an empty rule being a version that grants no
  letter: the answer is the letters all of those versions hold, cut to the limit, and the walk
  ends. No record is read, the row's or the switch's;
- **otherwise the switch decides whether the node lets through.** Where it refuses, the profile
  gets nothing and the walk ends, and no record is read;
- **where the node lets through**, the limit is cut to the letters all granting versions of the
  row hold, where any stand; to the letters of each standing version of the row that takes the
  rule off, `off:deny` holding none; and to the letters that each standing `sw:inherit` version
  holds for the profile, a profile such a version does not name holding none. Then the walk goes
  on.

The switch refuses where any standing version of it refuses, which is why versions that differ
so refuse, and where it refuses for a lost version or a text out of form; a node with no version
of the switch lets the walk go on. The switch's letters are read for a profile the node names
as well, wherever its row lets through, and never where its row answers. A switch with no
version cuts nothing, and neither does a row with no version. An answer left with a rule's
switches and no verb grants nothing, as a rule of that shape does, `DESIGN.md` §137. Where a limit
is what refuses, the decision names the node that set the limit, a node whose record cut among
them. Nothing is looked up that the walk does not read already. So the result is never wider
than any standing version, for every letter, which is the condition S9 set, and the walk is
that model's walk wherever every row and every switch stand in a single version and nothing is cut.
It rules out combining the versions at the node alone, which cannot cut what arrives from
above. The combination was run with the whole mask and a rule's switches in some versions and
not in others, in 16,974,336 of 16,974,336 pairs without a recorded mask and in the pairs above
with it, and through a rule on a group between the root and the node, where a rule taken off
beside a narrowing version gave what arrives from the group's rule cut to the narrowing letters
in 16 of 16 cells of each of two sequences (`measured-rules-owed-forms` 3, 4). *Held by*
`AclResolverTests`, against the meanings of the measured code and, over every pair and every
triple of masks of a small alphabet with recorded masks, against the condition.

**What a write records** (the same decision; as run in the model and on files but where a mark
differs, `measured-rules-across-levels` 1). Each is the operator's act at the node, in the write
of its dialog's OK (§629).

- **A row set to letters, or to no letter**, records nothing.
- **A row taken off, where the switch lets the walk go on once the OK is written**, is `off:`
  and the mask recorded for the profile (below). Each standing
  `sw:inherit` version that the tab showed and that holds other letters for that profile is
  written anew by itself, with that profile's letters changed and every other profile's kept.
- **A row taken off, where the switch refuses once the OK is written**, is `off:deny` (below).
- **The switch turned to refuse** is `sw:deny` alone.
- **The switch turned to let the walk go on** is `sw:inherit` with the mask recorded for
  every profile. Every standing version at the node that takes
  a rule off, of every profile, that the tab showed and that holds other letters than arrive is
  written anew by itself, as `off:` and the letters, `off:deny` among them: the switch let
  nothing through before, and the tab shows each of those profiles' inherited rows live at the
  press. It is written only where the store reads otherwise than as a switch that lets the
  walk go on by a single standing version or by none: a switch never set, ticked and cleared
  again on a tab, writes nothing, and a switch in several versions that let the walk go on is
  written anew when ticked and cleared.
- **The press that settles** (§657) writes anew each standing version at the node that lets the
  profile through and holds other letters for it than arrive: each version of the row that
  takes the rule off, `off:deny` among them, and each `sw:inherit` version, with that profile's
  letters changed and every other profile's kept. It is offered only where the switch lets the
  walk go on. It is in the measured code and was in no run of it, on files or in a
  search.

**A mask recorded is what the tab showed arriving at the press, cut to what arrives at the node in
the write's own reading** (a later decision; in no run of the measured code, which composes every
act at its write). It is never more than the operator saw, and never more than arrives when the OK
is written. The reason is a narrowing above that landed under the open dialog: a mask recorded as
the tab showed it would hold more than arrives at the write, and that narrowing could then be undone
by a widening made apart once copies meet, with the node's version holding the wider mask. A mask
cut to nothing is `off:` alone, which follows what arrives later, and stays apart from `off:deny`.
Where two presses on a tab write the same version anew, as a limit settled and the box cleared, it
is written once, with what both presses showed arriving, cut the same way. This corrects the bullets
above, which had the letters as the tab showed them at the press and no cut. *Held by*
`RuleActsTests` and
`RuleWritesInRealDatabaseTests.A_write_plans_against_the_very_reading_its_act_was_composed_over_and_cuts_to_what_arrives_in_it`.

**The write above** (the same decision; as run, `measured-rules-across-levels` 1, 9 and 12, but
where a sentence names another mark). After any write at a node that has nodes below, whichever
act made it, a row set, changed or taken off, a single version of a rule in several versions
removed, the press that settles, or the switch turned either way, the same write goes on to the
nodes below, the nearer node first. A row's act concerns its profile, and a switch's act every
profile. A node below is looked at for a profile where, once the act is written, its switch
does not refuse, its row for the profile does not answer by itself, and it holds a version that
lets through. Its versions for that profile are written anew where all of these hold:

- what arrives at the node for the profile differs before and after the act;
- every such version held everything that arrived before, and where nothing arrived, recorded
  nothing itself. A single `off:deny`, a single version that was cutting already, or a single
  version that recorded letters where nothing arrived, leaves every version of that node for
  that profile as it is, the switch's among them. Where nothing arrived without an act above
  having emptied it, as after a loss above or a refusal above merged in, a version that
  followed and a limit look alike, and renewing every such version would let a repair above
  lift a limit that nobody settled at its own node (§639). A version that followed until an act
  above emptied what arrived recorded nothing at that act, and follows (this document's own
  where nothing arrived, and stricter than the measured code, which takes every version there
  as following; owed to the model, §644 item 24);
- the tab's reading held those versions at every press concerning the profile, at a node that
  was below the dialog's node in that reading, and no version of the profile's row or of the
  node's switch stands at the node at the OK that the reading did not hold (this document's
  own, as §629 has a new version name what the tab showed; in no run, since the model composes
  every act at its write). A node left for that reason is named in the log (§629).

**"Before" is the reading of the write's own turn without the act, and "after" is that reading
with the act's versions and the renewals made so far** (this document's own; in the runs the
tab's reading and the write's are a single reading). With "before" taken from the tab's
reading, a version made apart that landed under the open dialog at a group between the dialog's
node and the node below would pass for a version that held everything, though it cuts by the
time of the OK. Each version is written anew by itself: a row's as `off:` and what arrives
after, a switch's with that profile's letters changed and every other profile's kept. Its
`after` names the version it renews and everything that version names, and never a version
made apart that stands beside it. A narrowing is carried as a widening is: the letters that
arrive after the act are written whatever they are, and under a switch turned to refuse above,
the versions below that followed are written anew holding no letter. **In a sentence: an act
above carries the nodes below that were following it, and no node that was being held back.** A
limit stays whole until it is settled at its own node. **Sealing the store again is no act
above** (as run): the row or the switch that the press writes for refused already while its
version was lost, so what arrives below is the same before and after the press, and the press
writes no version below anew (§625). **Removing a single version of a rule in several
versions** writes each version left with its own text, so a version that takes the rule off
keeps its letters, and the write above follows (read from the measured code; drawn in no search of
the project's). In the run, a row of the root widened above 20, 200 and 2,000 nodes that let
the profile through wrote a version and a deletion record for each node, beside its own, and
every node followed (§643). *Held by* `RuleActsTests` and `StoreWriteShapeTests`, against the
writes of the measured code (`StoreWriteVectors.json`); what holds the build is under "What is
built of the write above", and the runs over synchronized copies beyond those named there are
left to a later commit (§644 item 24).

**What is built of the write above.** An OK on the KPG tab of a group's dialog carries its act
into the versions below that follow it, in the same act, and so in the same write and under
the same summary entry as the versions at the node (`RuleActs`). The nodes below are the nodes
under the dialog's node in the tree of the write's own turn, the recycle bin among them where
it lies, and the plugin's store and everything in it left out (`DatabaseNodes`), taken by their
distance below the node and by identifier within a distance, so the order in which the tree
hands them over changes nothing. **A node in the recycle bin is carried as any node is**, by
the bin's own place in the tree, so a narrowing above reaches it while it lies there, and a
node restored under a group gives what arrives there cut to what arrived by the bin's path,
which shows on its tab as a limit where the group gives more (§639). The Profiles page's count
still leaves the bin out (§636). A node is looked at for each profile for which what the
dialog's node gives changed with the act, since what arrives below changes for no other
profile. Its versions for that profile are written anew where all of these hold: its switch
lets the walk go on and its row neither refuses nor answers by itself; what arrives at it in
the write's reading differs from what arrives with the act and the renewals made so far laid
over that reading; every version of its row that takes the rule off, and every version of its
switch, held everything that arrived before, and where nothing arrived recorded nothing, so
that a single `off:deny`, a single version that was cutting or a single version recording
letters where nothing arrived leaves them all (owed to the model, above); and every version
standing there for the profile, of the row and of the switch, is a version the tab's reading
held at every press concerning the profile. **The tab keeps those versions with each press**:
the identifiers of every standing version at each node below that holds a version letting a
profile through, read as the tab opens and again where a record lands, so a press keeps what
the tab showed when it was made. Where a row and the box were both pressed, a version is
renewed only where both presses kept it. **A node left because the tab had not held a version
standing there is handed back** as no press of the operator's, and the log names it by that
node in a line of its own, so that the log tells why a change above did not reach it; the
message after the close holds nothing of it (§629). A row's version is written as `off:` and
what arrives after; a switch's version once, with each profile that follows changed and every
other kept; and each names the version it renews and everything that version names, which the
same write then deletes as replaced (§627). A renewal that does not fit a string is left as it
was, too long, and handed back at the node it is at as no press of the operator's: the log
names it by that node, and the message after the close, which tells of presses alone, holds
nothing of it (§627, §629). An entry's dialog, the Add Entry dialog among them, has nothing
below it. **The rules are worked out once for the act**, the node's parent and the tree made
from them, and the tree is walked only where a version of the reading lets a profile through,
only below the dialog's node, and only where the act changes what the node gives; a node's
identifier is written as text only for a node that lets through and the groups above it. What
it holds KeePass's thread for is in §643. *Held by* `RuleActsTests`, each condition alone,
every mask of a small alphabet above and below, masks that hold each other and masks that do
not, a loss above set again and a box ticked and cleared above a limit and a version that
followed, a rule set where nothing arrived, and a row's press and the box's keeping different
versions; `StoreWriteShapeTests`, the writes above of `StoreWriteVectors.json` with the file
after each act, the histories §639 keeps among them; `GrantSheetTests`; `GrantSheetWritesTests`,
every sequence of up to two presses above nodes below holding, for two profiles, a version that
follows, `off:deny`, a limit, a switch, two versions made apart, a node below a node below and a
version the tab did not hold, the table after OK the table before it and what each node below
holds after OK worked out by letters in the test; `DatabaseNodesTests`, the tree holding the bin
by its own path where the count leaves it out; and `GrantEditorInRealDialogsTests`, over a real
group dialog, a version merged in under it and named in the log, a node moved by KeePass before
the OK, a reading made again before a press, a narrowing above a version that follows and a
limit, the box ticked and cleared, a node in the bin carried along a narrowing and a widening,
the sequence of §639 through the bin over real databases merged by `PwDatabase.MergeIn` both
ways, and a renewal too long named in the log by its node and told in no message.

**Considered and not taken, as measured: a version that names what stood above it**
(`measured-rules-across-levels` 3 to 6 and 11, where it is form A). A version that lets through
would name the versions its writer read above the node and in the node's own switch, and the
node would let the profile through only where exactly those versions stand, and refuse
otherwise. It reached the same counts of none, and closes by refusing what the form taken
leaves (§639). It refuses wherever anything above was changed apart, a narrowing among them:
a copy gave less in 128 to 424 histories of 200,000 in which the reading without a form had
stayed within both copies, a profile given nothing in all of them but a single history, where
the form taken gave a profile nothing in 7 to 71, and in 1,232 against 218 in the longer
histories; it refused after a move in a single copy, and on files in 16
cells of a sequence in which the copies differ by a rule above changed apart. It names
versions that §627 deletes, and rules of different nodes would meet after every merge, which a
record for each node and profile set out to avoid. **Considered and not taken, by reasoning:**
the version above naming the versions below that its writer had read, under which a narrowing
above renews nothing; writing anew, at any write of the plugin's own, every version that holds
more than arrives, which is a repair after a synchronize (§630); a limit to what the row gave
before it was taken off; and a write above that also carries a node that was being held back,
under which a limit nobody settled would open letter by letter from above.

**Removing a version where several stand writes again each version that is left** (fixed as
measured, `measured-rules-per-profile` 5; S9). For each version left, the plugin writes a
version with the same text whose `after` names the removed version and everything it names,
and the version left and everything that version names. Removing a version of two is the same
write as setting the row to the version kept. It writes only records of a kind that exists,
and every version it writes restates a version that stood, so the result is never wider than
the versions that stood. In the run, a removed version came back by no merge, in 17 of 18
cells, where the eighteenth is the import into the copy that stays stale; a removal in a copy
and a change of the same row in another converged after Synchronize both ways; and no cell of
the five sequences run gave the profile a letter that neither copy gave.

**Its cost is narrower: two removals made apart undo each other.** Where two copies each
remove a version that the other copy kept, each writes again the version the other removed,
and after the merge every version stands again. The profile gets what all of them allow, which
was nothing in both sequences run, and the operator removes versions again in a copy that
holds them all. So a removed version stops standing in every copy the write reaches, except
where another copy removed a different version apart. No form that was run has both: removals
made apart that hold, and a result that is never wider.

**Considered and not taken, as measured.** A single version that names the removed version and
holds nothing of its own lets two such removals converge, and is fail-open: where two copies
each remove the version the other kept, nothing stands but the two removals, the node does not
name the profile, and the profile got what arrives from above, which neither copy gave, in 16
of 18 cells. A single version that names the removed version and holds what the versions left
allow together gave the answers of the form taken, and adds a row to the table that nobody
made.

**Versions of the switch made apart refuse while they differ** (reasoning from what all versions
allow). For a profile the node does not name, a switch that refuses allows nothing and a switch that
inherits allows what arrives from above, and what both allow is nothing. It was run in a single
sequence (`measured-rules-per-profile` 7): a switch turned on in a copy, and turned on and then off
in another, left the merged file refusing the profile the node does not name, as the copy with the
switch on did, and again with a group's switch, in 16 of 16 cells (`measured-rules-owed-forms` 4).
Two versions that both let the walk go on and hold other letters for a profile do not refuse: the
reading cuts to the letters both hold (read from the measured code). What the grant editor shows for
a switch in versions that differ, and what a press on it writes, is in §657.

**The X takes the rule off, also under a switch that refuses** (a later decision, which
reverses the decision kept below under "Reversed"). Where the node's own
switch refuses once the OK is written, the X writes `off:deny`: a rule taken off that lets
nothing through until it is written anew at its node. It is kept apart from `off:` alone, a
rule taken off under a switch that let the walk go on while nothing arrived. Both let nothing
through, and they differ at an act above: `off:` alone was following what arrived, and is
written anew when something arrives, while `off:deny` was let through by nobody, and is left.
So no row is without an X. An empty rule is a rule the operator chose, or that sealing the
store again wrote, and its X takes it off like any row's. **The sequence that the reversed
decision closed stays closed, by the reading**: with the row taken off as `off:deny` in a copy,
and the switch turned off in another copy that still held the row, the row lets nothing
through once the copies meet. Measured (`measured-rules-across-levels` 3 and 4): the sequences
of a row and a switch changed apart at a single node, W1 to W8 there, on files, every merge
both ways, with the removal written as the reversed decision had it, taken off at the second
removal, and taken off for real, gave nothing beyond both copies in any of their 432 cells,
and in the model none of 200,000 histories did for any of the three. **It is wider than
the reversed decision in a single place, by design.** A copy that holds the `off:deny` and
turns the switch off writes it anew with what arrives, since at that press every inherited row
on the tab goes live, that profile's among them, and the profile then gets what arrives, where
the empty rule kept it out. That is a press in a copy that had seen everything, and no merge
is in it. On files it is the sequence W6, in 16 cells and its import cell: the copy that turns
the switch off gave read and write before the merge, and the merged file gives that. In the
model the form with a real taking off gave more than the reversed decision in 19 of 200,000
histories, and in 117 of the longer histories, none of them with a merge beyond both copies.
The operator who wants such a profile kept out gives it a rule on the same tab before OK.
**Considered and not taken, as measured:** the form with the removal written as the reversed
decision had it, which reached the same counts of none and was never wider than the reversed
decision in any history drawn, and which keeps a row that cannot be taken off while the switch
refuses. **What `off:deny` is for was run on files only in part.** The sequence is: nothing
arrives for the profile, the node refuses profiles it does not name and holds a row for the
profile, the row is removed in a copy, the switch is turned off in the other, the copies meet,
and the root is then widened for the profile. With the removal recorded as `off:` alone the
write above would write the version anew and the profile would get at the node what the root
gives, though no operator ever let it through there; with `off:deny` it gets nothing. On files
the sequence was run with read arriving from the root, where the version cuts without the
mark, so the sequence as given here is owed (§644 item 24).

**Which text an X writes follows the switch as the OK leaves it** (this document's own; in no
run). An X pressed under a switch that refuses, with the switch then turned off on the same tab
before OK, writes `off:` and what arrives, and an X pressed under a switch that lets the walk
go on, with the switch then turned to refuse on the same tab, writes `off:deny`, whichever of
the two presses came first. It rules out a mask recorded under a switch that the same OK turns
to refuse. **And it follows the switch the tab showed at the press**: an X pressed where the tab
showed the box ticked, with no press on that tab clearing the box, writes `off:deny` also where
the write's own reading finds the switch letting the walk go on, as after a version of the
switch that reached the database under the open dialog. What the tab showed arriving under a
switch that refuses is nothing that the node let through, so no mask of that press is recorded,
and the row lets nothing through until it is written anew at its node. *Held by*
`GrantSheetTests` and `RuleActsTests`, each order of the two presses, and
`RuleActsTests.A_rule_taken_off_under_a_box_the_tab_showed_ticked_is_off_deny_whatever_reached_the_store_under_the_dialog`.

**Reversed: under a switch that refuses, removing a row wrote an empty rule.** The three
paragraphs that follow are that decision as it stood, with what was measured of that form, and
what the first of them lists as considered and not taken, a version that takes a rule off
stating the switch it was made under, is what is taken now, in the form of what it saw.

**Under a switch that refuses, removing a row writes an empty rule** (a later decision). Where
the gate reads the node's switch as refusing any profile the node does not name, removing a
profile's row is stored as a version that grants nothing, and not as the rule taken off. While
the switch refuses, the two read the same at that node: the profile gets nothing. They differ
once another copy, apart, turns the switch off. A rule taken off would then let through what
arrives from above, which neither copy's table gave, and an empty rule still grants nothing,
which is what both tables allowed (§639). The row shows as an empty rule until the operator
removes it again (§657). Where the switch lets the walk go on, removing a row takes the rule
off. **Considered and not taken:** accepting the sequence as the cost of rows that do not
meet; and a version that takes a rule off stating the switch it was made under, with the
reading refusing the profile where the switch has changed since.

**The run confirms it** (`measured-rules-per-profile` 7). With the removal written as an empty
rule, the profile got nothing at the node after the merge in 18 of 18 cells, in both
directions, by every KeePassLib method and every KeePassXC release run. Written as the rule
taken off, which was run as the control, the profile got read and write from the root, which
neither copy's table gave, in all 16 cells that are not imports.

**Removing an empty rule again while the switch still refuses writes nothing** (as run,
`measured-rules-per-profile` 1, 7). Stored as an empty rule, that removal changes nothing, so
no version is written and the row stays an empty rule. A removal takes the rule off only once
the switch lets the walk go on. Taking the rule off at the second removal is the same sequence
a step later, and was run: with the row removed and synchronized, removed again in a copy, and
the switch turned off in the other, the profile got read and write, which neither copy gave,
in 16 of 18 cells, and with nothing written at the second removal it got nothing. The decision
names the row as there until the operator removes it again. The run holds beside it that such
a removal, made while the switch still refuses, has to write nothing.

**Rows changed apart across nodes no longer add up to more than either copy's table gave, but
in the classes §639 keeps.** A profile's rule, a node's switch and the rules of another node
are objects of their own, so a change to each, made apart, holds in full, and without a record
of what arrived a rule taken off at a node and a rule widened above it, apart, ended with the
profile getting at the node what neither table gave. This paragraph had that as what S34 still
held, with the decision on a removed row answering the single node alone. The record of what
arrived closes both (above), and §639 has the sequences, what is closed and what stays.

**An object refuses** where the file shows that a version existed and holds its text nowhere: a
deletion record tells of a version of the object that no standing version names as replaced and
no summary entry in force carries, or no version stands and a version is present beside a
deletion record naming it. A profile's rule that refuses answers for that profile at the node
as an empty rule does: the profile gets nothing there, and nothing below but where a nearer
rule answers first, as under any rule. This corrects "nothing at the node and below", which
read as overruling a nearer rule. A switch that refuses in that way reads as refusing: the node
grants nothing to a profile it does not name. In the whole-rule form the node refused for every
profile; with an object for each profile the refusal is that profile's alone. Measured
(`measured-rules-per-profile` 8): a version of a row, deleted for good with its summary entry
and with deletion records for both, stopped the store; the listing named the lost row alone;
and sealing the store again wrote an empty rule for that row and left the other profile's row
at the same node as it was. A lost version of the switch was written as the switch refusing.
**Where the store refuses as a whole after such a loss, the cause is no row that refuses.** In
the parts run since in which a single row's newest version was deleted by hand, the store with
a record for each profile stopped where the whole-rule form ended wider: the other row's new
version stood with no summary entry present to carry it, which stops the database as §624 has
it for any decision that is not restated (`measured-rules-cut-parts` 3 and 6).

**What a deletion record tells of a rule's version** (S8; this document's own where it differs
from the reading that was run first). A deletion record tells of a version of a profile's rule
where its identifier begins with the tag of `version` under the store key, holds in bytes 4 to
8 the subject hash of `rule:` and a node, and holds in bytes 9 and 10 the tag of a profile's
name the file knows (§609). It tells of a version of a switch where it begins with that tag,
holds the subject hash of `switch:` and a node, and holds 0 in bytes 9 and 10. A node is a
group or an entry of the tree, wherever it sits, or the node of an object of type `rule` or
`switch` that a version present, or an item of a summary entry in force, names. The file knows
a name from a record of kind `profile` or `removed` present, in force or not; from an item
`removed` of a summary entry present; and from the object of type `rule` of a version present,
in force or not, or of an item of a summary entry in force. Knowing a node or a name only lets
a deletion record refuse. **A tag that meets no name the file knows tells of nothing**, until
the file knows the name. So a row of a name off the list whose records and items are all gone
refuses nobody, and refuses its profile from the hold on in which the file knows the name
again, as where the name is added. Measured (`measured-rules-owed-forms` 5): a name can be
unknown to the file only where no rule names it, so it is granted nothing anywhere; no read was
wider where the name was unknown, and the row refused as soon as the name was added again.
**A deletion record under a tag tells of every name of that tag that the file knows**, so two
names whose tags meet are both told of, and both rows refuse, which is narrower. Measured, with
the reading corrected to that (`measured-rules-owed-forms` 6): over two names whose tags meet
under a key, found by a search, with a version of either row deleted for good, both rows
refused, sealing the store again wrote both as empty rules, and no read of 22 was wider. The
reading as first run kept a single name for a tag, in an order that changed from a process to
the next, and was fail-open: the lost version of the name it did not keep read as a rule never
made, and the profile got what arrives from above, in 4 of 6 reads. No profile got the other
profile's rule under any reading, since a version and an item carry their object, and only a
deletion record is read by the tag. The reading computes the tag of every name it knows, so it
can log two names whose tags meet.

**A version names what it replaces apart from the lost versions it settles** (a later decision,
measured in the model, `measured-lost-settled` 6). A version of a profile's row or of a node's
switch holds, beside `object`, `after` and `doc`, a member `lost`. **`after`** names the versions
of its own object it replaces: each version of that object that stood when the write was
composed, as a record in force or an item of a summary entry in force, which the tab showed, and
every identifier those name in `after`; the writer knows each to be its own object's, since each
stood with its content. **`lost`** names the lost versions at its place that the write settles
for its own object: each identifier the tab showed lost for that row or that switch, and every
identifier the versions it replaces name in `lost`. The member is absent where it names nothing,
so a record written where nothing is lost keeps its bytes. Each list holds identifiers of 32
lowercase hexadecimal characters, each once, ascending by ordinal, separated by a single space,
and no identifier is in both. **Every later version carries both on**: a version that replaces
versions names in `after` what they name in `after` and in `lost` what they name in `lost`; a
renewal by the write above names in `after` the version it renews and that version's `after`,
and in `lost` that version's `lost`; removing versions where several stand writes each version
left naming in `after` the versions removed, the version left and what each names in `after`,
and in `lost` what each names in `lost` and the lost versions the X removed; and sealing the
store again (§625) writes the same for each object that refuses for a lost version, built by
`SealActs`, and a rule set over the seal's version names it and carries its `lost` on. The item
`version` of a summary entry restates `lost` as the version holds it, and holds none where the
version holds none.

**What a deletion record under the tag of `version` refuses.** Where an item of a summary entry
in force carries its identifier as `id`, nothing: the version's text is held. Otherwise, **where
the record of a version is present under its identifier**, beside it, it is told of by that
version's own object alone, with nothing hashed, and refuses that object unless a version in
force of that object names it in `after` or in `lost` (read from the measured code, which counts
such a version as lost). **Otherwise it tells of every object at its place that the file knows**:
the row of every name of its tag at its node, and with 0 in bytes 9 and 10 the object `rule:` and
the node with no `/` where the file holds a version of it; or, under the subject of a switch, the
switch. Where a version in force of an object it tells of names it in `after`, it refuses none of
them: it is known whose version it was, the namer's. Otherwise it refuses each of them but those
of which a version in force names it in `lost`: it is settled for that object alone. A version
of an object at another place that names the identifier refuses nothing and answers nothing, and
no writer names so. **What that closes, measured** (`measured-lost-settled` 3 and 4). The
measured reading answered a deletion record for every object at its place once any version in
force named it in `after`, where the acts named the lost versions they settled in `after` too,
so a lost rule of a name read as never made once a row of a name whose tag meets it was set,
and that name got what arrives from above; a reading that answered for an object only where a
version of that object named it closed that, and every ordinary edit of a row then left a
deletion record that every other row of its tag refused for, and over two copies a Synchronize
ended beyond both. With the two told apart, no history of a million over two and three copies,
a tag of 0, a person's deletions and Seal again failed, and every merge beyond both copies was a
merge the reading that knows whose each version was ends at as well. Which deletion records the
reading hashes a subject for is in §620, and what the ten keyed bytes of an identifier cannot
tell apart is in §609.

**What replaces what.** A version replaces only versions of its own object: an identifier in its
`after` that a version of another object, or a record of another kind, carries is passed over, and
that version stands. A version that a version in force of the same object names, as a record or as
an item of a summary entry in force, is deleted with a deletion record at the plugin's next write
(§627), where a version that stays in force names it and every identifier that it names itself is
named by a version that stays; a version that only an item of a summary entry carries is restated no
more on the same condition and stays restated otherwise, and a version that only a version out of
force names is left. A write whose version names a version of the same write is refused (§610).
**`lost` replaces nothing.** An identifier named in `lost` has no entry in the copy whose tab
showed it lost, and keeps the deletion record it has there; the write deletes nothing for it and
adds no deletion record for it. A version that later turns up under such an identifier with its
content, and that no version names in `after`, stands beside the versions that settled it, as a
version made apart does, and with them can only narrow.
This corrects "a present version": a version present beside a deletion record is out of force,
by step 1 of §623, and replaces nothing. Every standing version is restated whole by the
summary entry.

**What a node's place does.** A node under its own rule keeps it wherever it is moved: a node
with a rule granting nothing under a root that grants gave nothing in every cell of 90 where it
exists, in the whole-rule form (`measured-store-as-a-whole` 3.2) and with a record for each
profile alike (`measured-rules-per-profile` 3). A copy of a node has a new identifier, no rule
names it, and it is read under the rule of the place it lands in (§639). A node whose version
lets through keeps that version where it is moved as well, and gives at its new place what
arrives there cut to what the version recorded at the old (§642). A profile whose rule at a
node is lost in every carrier, with no trace, is a profile the node does not name. Measured
(`measured-rules-owed-forms` 5): a row's versions and every summary entry deleted for good
with no deletion record, or with the deletion records cleared, read as a rule never made, and
the profile got what a rule above gives it, in 26 of 300 reads, the list of names playing no
part in it; no read was wider where a deletion record stayed. **A replaced version was a third
carrier of a row by accident, and is none once a write deletes it** (reasoning, §627): where
the replaced versions of a row were still in the file, a standing version lost in both of its
carriers left an earlier version standing, and with them deleted the row reads as never made
once every deletion record is gone as well (§639). An entry's history holds no rule, and
restoring an earlier version of an entry changes none, since a rule belongs to the node by its
identifier (reasoning).

**No ceiling is kept on the profiles ruled at a node** (a later decision; S21), as none is kept
on the list of names (§616). No write of the plugin's own refuses a row for the count of rows
at its node, and the reading cuts nothing short. A ceiling could not hold across a synchronize
in any case, since two copies can each add rows apart (reasoning). A rule document held 31
profiles at most, `DESIGN.md` §136, which was what its reader took and no property of a record
for each node and profile. The grant editor's table scrolls its rows (§657), and each row is an
item of the summary entry, which the room counts (§627, §643).

**Where a rule is read** (this document's own). The rules of a reading are worked out from its
standing versions for the hold that first asks for a rule, and are kept with that hold's reading
and no longer: nothing of a rule is kept between two holds (§620), and a hold that asks for no
rule, as the hold a connection is judged in, works none out. A change of the API's asks its
first question, before its turn, in the single hold that finds its node, as a read does, since a
hold for each walk up the tree would read the store once for each. The grant editor's tab reads
its node's rule, the names and what arrives from above from a single reading as it opens, and
again where a record lands while the dialog stands. The log names a change of rules at a node
dialog's OK by the first characters of the node's identifier, each profile set or removed and
the switch, and never by a title, which would put a title into a file outside the database; the
audit holds no line for a rule changed, `DESIGN.md` §120. *Held by* `StoreReadingBoundTests`,
`RouterAuthorizationTests` and `GrantPanelTests`.

**What is built.** The records of this section, their reading, the walk, and the write of a node's
own dialog with the write above. `RuleText` composes and reads every text of the table strictly,
and an object in any other text than its form. `StoreInForce` holds, for every object of whatever
type, its standing versions and the versions lost: a version present beside its own deletion record
that no version in force of its object names in `after` or in `lost` and no item of a summary entry
in force carries, and for a row and a switch a deletion record whose content is nowhere, judged at
its place as above. `StoredRules` works a reading's
rules out once, each node's by its identifier, and `NodeRule` holds what a row and a switch come to:
whether each refuses, what the granting versions all hold, and what each version that lets through
recorded. `AclResolver` makes the single walk with a limit of every letter, and the router, the
handlers, the routes that answer what a client may do at a node and the grant editor ask that walk
and nothing else. A refusal names its node and its reason: a rule lost, a rule that could not be
read, rules made apart that do not all grant, a limit that does not let the verb through, a switch
that refuses, or no rule anywhere. `RuleActs` composes the act of an OK over the reading the write
is planned against: a row set, a rule taken off as `off:` with its mask or as `off:deny` by the
switch as the OK leaves it, the switch turned either way, a single version of a rule in several
versions removed, the press that settles, and the versions at the node that each of those writes
anew, every mask cut as above. A version that would not fit a string is left as it stands and
reported, and the rest is composed as though it had not been pressed (§627). `KdbxNodes` reads a
node's rules by its identifier from the rules it is handed, so a rule stays with its node wherever
the node is moved, and a copy, an entry made from a template and an earlier version of an entry hold
none. Nothing of a rule is written on a group or an entry. The write above is built with them,
as "What is built of the write above" has it, with what the sheet keeps for it of the versions
below (§629). **The runs of rules changed apart over synchronized copies** are
`StoreRuleMergeTests`: copies of a real database that the product wrote, every rule written as a
node dialog's OK writes it, the tab's own sheet pressed, and the copies brought together by
`PwDatabase.MergeIn` under each method in each direction, each merge held to both copies by
capabilities; §639 has what each group of its sequences holds, and §644 item 24 what stays owed.
**Left to a later commit**: every cost but what an OK above many nodes, and a hold and an OK
after many renewals, hold KeePass's thread for (§643, §644). **A version's `after` and its
`lost` are built as this section has them**, as the model has them (`measured-lost-settled` 6):
`RuleActs` names the versions a write replaces in `after` and the lost versions the tab showed
in `lost`, every act carrying both on; `StoreChecked` reads `lost` once with the version's
checked form, as `after`; `StoreInForce` answers a deletion record by its place, a version in
force of an object there naming it in `after` answering for every object at the place and in
`lost` for that object alone; and `StoreReplaced` reads nothing of `lost`. **Where the build
reads otherwise than this section** (each stated for a decision, and none changed here).
The file knows a name and a node from the items of every summary entry present, where this
section has the items of a summary entry in force; knowing more only lets a deletion record
refuse. And the log names a change of rules by the node and that the rules were written or not,
and each change or version that was not written by its profile, its node and its reason, and
not each profile set or removed. *Held by* `RuleTextTests`; `StoredRulesTests` and
`AclResolverTests`,
against the meanings of the measured code (`tests/Shared/StoreMeaningVectors.json`), by letters and
by capabilities; `AclInheritanceTests` and `AclDecideAnyTests`; `StoreInForceTests` and
`StoreReadingTests`; `RuleActsTests`, and `StoreWriteShapeTests` against the writes of the measured
code (`StoreWriteVectors.json`); `RouterAuthorizationTests`, each refusal with its reason and node;
and over real databases `AclInRealDatabaseTests`, a group moved keeping its rule and a duplicate
holding none among them, `RuleWritesInRealDatabaseTests`, `StoreSourceNodesTests`,
`StoreOnEveryRouteTests` with a rule the product wrote and with what a merge of rules changed
apart brings,
`MergeInRealDatabaseTests.Rules_changed_apart_end_at_what_the_lower_version_recorded_under_every_method_both_ways`,
and `StoreRuleMergeTests`; and in a real KeePass by `LiveRuleSeededStatesTests`, a version
removed, a limit settled and a node below carried by an OK above, each read by a client of the
gate and from the file.

### Renewal answers §618

A standing answer to a renewal is a version, as a rule is, of an object of its own. Two answers
made apart give the stricter, so a lift made without having seen a refusal loses to it. An
answer for this session has no record and stays in the plugin's memory, `PAIRING.md` §312.

**The object is `renew:` and the client key**, 64 lowercase hexadecimal characters, as measured
(fixed for the answers, S7). The key is the client's and stays across renewals, so an answer
holds for every certificate of the client, a certificate that a renewal issued at a gate not
yet told of the refusal among them. The subject of the identifier is the whole object, with 0
in bytes 9 and 10, as for a switch (§609). **An object of type `renew` in any other text is the
object of nothing** (§658): its versions are records, restated and counted by the room, and no
answer is read from them. It rules out an answer by certificate, which ends with the renewal
that issues the next certificate, as a revocation by certificate ended wider in the runs of
§615, and an object that grows with every certificate a client gave up.

**The text of a version** is one of three, composed and read in a single place, strictly and
totally, as a rule's text is (fixed for the answers, S7):

| Answer | Text |
| --- | --- |
| Refused forever | `refuse:forever` |
| Refused until a moment | `refuse:until:` and the moment as the wire writes it, RFC 3339 in UTC to the second, 20 characters |
| A lift, **Allow renewal** | `ask` |

A text is in form only where composing what was read gives the same bytes: a moment that is no
date, a fraction of a second, an offset, a letter in lower case, a space or anything after the
text is out of form. The moment is the operator's choice, the first midnight of the chosen day
where the operator is, written in UTC: content, and no time of a write. `ask` states what the
lift gives, the operator asked again; the design paper's "allowed" would read as a renewal
approved without a dialog, which no answer gives.

**What the standing versions give together** (fixed for the answers, S7):

- **The strictest standing version is the answer**: `refuse:forever`, then the later moment,
  then the earlier, then `ask`. Two moments are compared as the texts the operators chose, the
  later the stricter. No standing version and nothing lost: the operator is asked, as for a
  client never answered.
- **A lost version refuses forever**, as a lost rule refuses (§617), whatever stands. A version
  is lost as §617 has it for a switch: a deletion record tells of a version of the object that
  no version in force names in `after` or in `lost` and no item of a summary entry in force
  carries, or a version is present beside its own deletion record and nothing in force names or
  restates it. A version deleted while a summary entry in force restates it is not lost: its
  item holds the text, and it stands. The lost version may have been a refusal forever, and
  asking again puts before the operator a dialog the operator asked not to see, which §639 row 5
  counts among the fail-open outcomes; refusing costs a client `409 forever` where the lost
  version was a lift, which is narrower.
- **A text out of form refuses forever**, whatever else stands, and stops nothing. A record of
  an answer that fails its check is damage, as every record is (§621).
- **Whether an answer until a moment refuses is a reading against the gate's clock**: it refuses
  while the clock is before the moment. The clock is read where a renewal is answered and where
  the clients page draws a row, and nowhere else: not by the store's core, not by what stands
  and what is lost, not by what a write names, deletes or restates. A clock behind refuses for
  longer; a clock ahead ends an answer until a moment early, and the renewal dialog is raised,
  which issues nothing without the operator's Approve.
- **An answer until a moment that has passed stays**: it stands and refuses nothing. Nothing
  deletes it for the moment having passed, since nothing is removed by a clock (§643), and
  nothing is removed for a client that is revoked or gone: a revocation refuses every
  certificate of the client already, and a certificate taken away leaves the client's answer
  untouched (§648).

**What a deletion record tells of an answer** (fixed for the answers, S7). A deletion record
tells of a version of `renew:` and a client key where its identifier begins with the tag of
`version` under the store key, holds the subject hash of `renew:` and a client key the file
knows, and holds 0 in bytes 9 and 10. The file knows a client key from the `client` of every
record of kind `client` present, in force or not, of every record and every item `revoked`, and
from the object of type `renew` of a version present or of an item of a summary entry in force;
a text that is no client key adds none. Knowing a key only lets a deletion record refuse, and a
key known again later tells from then on. A deletion record at the place of a version present
of the object is that object's with no subject hashed; any other that holds 0 is tried against
`renew:` and each client key the file knows after the nodes the objects name and before the
tree is walked (§620).

**A lost answer whose client key the file no longer knows tells of nothing** (as measured,
`papers/ground/measured-seal.md` 6 and 8 item 9). Where a deletion record of an answer whose
text no file holds is in the file, and the client's admission is then deleted by hand, so that no
record and no item names the client key any more, the deletion record tells of nothing, and the
answer it refused is not refused. The client is admitted nowhere in that copy, since its
admission is gone, so no renewal of it is asked. The measurement found the class in its random
histories, apart from the seal, the shortest a lost answer and the admission deleted with its
deletion record; it is a class of its own beside those of §639, and nothing is built for it.

**What a version names is what its writer was shown** (fixed for the answers, S7):

- **A version names in `after`** each standing version of the client's answer that its writer
  was shown and that still stands, with everything those name in `after`, as a rule's version
  does (§617). A refusal given on the renewal dialog names what the reading before the dialog
  held, and a lift what the clients page showed at the press. A version that arrived by a merge
  while the dialog stood is named by nothing, stands beside the new version, and the stricter
  of the two is the answer, so a lift made without having seen a refusal loses to it.
- **A lift names in `lost`** each lost version it was shown that is still lost, with everything
  the versions it replaces name in `lost`, none of them in `after`. **`lost` replaces
  nothing**: a version settled there that turns up again with its content stands beside the
  lift and refuses.
- **A refusal is given only where the reading asks**, so it never settles a lost version and
  holds no `lost`. A refusal given after a moment passed names the passed answer, which stands,
  and replaces it.
- **A lift with nothing left to name comes to nothing**, as a certificate taken away with nothing
  left to end does: every version its press was shown was replaced since. It writes nothing.
- **A version is composed only where its content and its item fit** the plugin's ceiling of a
  string (§627). By the client key each further answer about a client replaces the earlier
  version, so a client has a single standing version however often it is answered, and its
  `after` reaches the ceiling at about 7,940 answers about that client.

**Sealing the store again** (§625) writes `refuse:forever` for an answer that refuses for a lost
version, as it writes an empty rule (built, `SealActs`, `RenewalText.Forever`). A lift of it, Allow
renewal, names it in `after` and carries what it settled in `lost` on, as every lift does.

**What is built.**

- **Read with the admission it renews**: the hold that reads a renewal's admission takes the
  standing and the lost versions of its client's object from the same reading, and their texts
  are read only where a renewal is answered, so the hold a connection is judged in parses no
  answer. The stored answer is told before the session's. A lost version and a text out of form
  are answered `409 forever`, and the log names the cause at each.
- **A renewal is in flight for a client**, by its client key, so a second renewal of a client
  whose first is still being answered is told `503 busy`, by the same certificate or another.
- **Written by the renewal dialog's Refuse menu, saved as an approval's record is**
  (`PAIRING.md` §312): a single turn of the writer that reads the store with every entry,
  composes the act over that reading naming what the reading before the dialog held, plans it,
  asks of every entry the plan touches (§626), applies it and saves, believed on the flag and the
  file (§627). Refused under Database Maintenance (§626). Where the write is refused or its save
  does not land, the change is taken back out and the answer holds for this session, and the
  operator is told; where the file does not verify, the answer stays in force, marked.
- **The condition of a renewal's write asks the answer once more**, in the write's own reading
  under the writer's lock: a refusal that arrived after the handler last read the store stops
  the approved renewal, issues nothing, and the renewal is answered with that answer at once.
- **Lifted by Allow renewal** on the client's own row of the clients page, an act the settings
  dialog's buffer holds and composes again at OK (§627), the operator's last press for each
  client naming every version each press about the client was shown, written at OK with the
  buffer's other records, marked and saved by the operator's save. An answer for this session
  is lifted beside it. Where the store takes no write in the session (§636), Allow renewal lifts
  an answer for this session and refuses a stored answer with the reason.

*Held by* `RenewalHandlerTests`, `ClientListTests`, `ClientListMenuTests`, `PendingChangesTests`,
`GateWiringTests`, and over real databases by `RenewalInRealDatabaseTests`,
`TabReplayOverRealDatabaseTests`, `GiveUpInRealDatabaseTests`, `MergeInRealDatabaseTests` and
`StoreRenewalMergeTests`, a lift written at OK and merged into before the save that keeps it
among them; and in a real KeePass by `LiveRenewalTests` and `LiveRefusalNotWrittenTests`.

**What the model ran** (`papers/ground/measured-renewal-answers.md` on `spikes/storage-survival`,
`run.ps1 -Answers` with its record in `recorded-answers\`): the three texts and the object above,
on a chain of four answers with a clock of three positions, forever beside the later moment,
two moments made apart, a lift naming what it was shown and what it settles, lost versions in
six ways, a renewal under the clock, and a client revoked and renewed apart, over two and three
copies, each merge by every method of KeePassLib and by four KeePassXC releases on real files.
No history ended below its ideal outside the classes §639 accepts. The vectors it printed are
`tests/Shared/StoreRecordVectors.json`, `renewalTexts` among them, and the stores whose names
begin `renewals:` in `tests/Shared/StoreMeaningVectors.json`, and A1 to A14 in
`tests/Shared/StoreWriteVectors.json`; `RenewalTextTests`, `StoredRenewalsTests`,
`StoreInForceTests`, `RenewalActsTests` and `StoreWriteShapeTests` hold the product to them, and
`StoreRenewalMergeTests` holds answers given apart in real databases merged by every method of
KeePass's. A7 holds Seal again, and is played whole past it, the seal's act composed over the
store at its write (§625).

### The recycle bin §619

**Release 0.3.0 keeps no record of the recycle bin** (a later decision). The evaluation's design
has a version that names the group that is or was the recycle bin, written when the plugin sees
a recycle bin at a write of its own (evaluation 4.2), for a former bin that a merge turns into
an ordinary group. That kind is not part of 0.3.0: the measured code holds no such record, and
nothing of it was run on the store.

**So a former recycle bin that becomes an ordinary group stays as it is today** (§639, row 14):
the plugin remembers no group as having been a bin, and after a synchronize or an import the
operator checks which group Database Settings names as the bin, `DESIGN.md` §138.

**The store makes such a record possible later.** It would be a version of an object of its
own, as a rule is, restated by the summary entry, and as a new kind of record it would move the
format number (§650).

---

## 4. Reading §620

A gate reads the store from its own copy at every request, as it reads the admission record
today, and nothing that grants is kept from a request to the next. The reading is a function of
what the database holds and of nothing else: every entry, each entry's history, and the deletion
records. Nothing is written at an open, at a read or after a merge. The steps are those of
`probe/read.py` with its history rule switched on, which is the reading every count of the store
as decided is a count of (`measured-decided-store` 1), with these differences, each named where
it applies: several store keys stop the gate (§649), a record of a kind the plugin does not know
stops it (§621), a version has to read a single way (§607, §608), and the kinds of §648, §649
and §650 are read as those sections have it. None of the differences was run.

**What may be kept between two readings** (S20). The reading runs with KeePass's thread held
still, `DESIGN.md` §119. Inside a single hold by a connection the tree cannot move, so the store
is read once there, and every question of that hold is answered from that reading. Between two
holds the plugin keeps a single thing: **the checked form of a version**, which is whether its
strings pass the check under a key the file holds, and what they parse to, the identifiers each
of its lists names among it, read once and searched at every hold after. It is kept by the
identifier of the entry and by the very string objects the version holds, name for name.
KeePass replaces a string object when a text changes and never changes a string object in
place, which `KeePassContractTests` pins, so the same objects are the same text, and any other
object is checked again. **Nothing else is kept as an answer**: no list of the entries that are
candidates, no key that stands, no record that counts, no summary entry in force, no word, and
no answer that admits or grants. **With a key its checked form gave, a reading keeps** the tags
of every kind under it and the hash of every subject it was asked for, each subject's text with
its hash, as long as the key is kept: a subject is asked for only where a deletion record under
the key carries the tag of a kind about it, and in a store under several keys only the `id` of
an authority present is. Every hold walks every entry of the database with its history, holds
each version to what is kept by its objects, and works out everything else again. The
reading of the last hold, its state and what the guard hides by, keys aside, is held until the
next replaces it, and answers nothing after its hold: it is kept by a number that names its
hold, `IReadHold.Stamp`, and no number is given to two holds. What is kept goes when the gate lets go
of the database at a lock or a close, and a store key among it is overwritten then, as the gate
key is, `CA.md` §208, even where a connection still holds what the database was served with, as a
pairing can for its whole budget: that connection reads afresh from then on and keeps nothing.
§632 states what of a key cannot be overwritten.

**Which deletion records a subject is hashed for, where the tag is a version's** (this
document's own, with the answers of the measured reading; S8, S20). Every version that a write
replaces leaves a deletion record under the tag of `version` (§627 step 3). By the sentence
above alone, a database in which a rule was ever changed would have the subject of every group
and entry hashed for its rules, once for as long as the key is kept, and looked up at every
hold after. S8 named that cost (§609), and this section did not. So a subject is hashed for a
deletion record under that tag only where nothing in the file already answers for it: no
version in force and no item of a summary entry in force names its identifier in `after`, no
such item carries it as its `id`, and no version is present under it, which is judged by its
own object (§617). For the deletion records left, the nodes of the objects the file names are
tried first, and the groups and entries of the tree are walked for their subjects only where a
deletion record is still not told of. Each hash is kept with the key, as above. The measured
reading hashed every subject first and passed over the same deletion records afterwards
(`probe/read.py`), so the answers are its answers and the order is turned: a store whose
deletion records under that tag are all of the plugin's own deletions hashes no node, at any
hold. It rules out a hash for each node in every database in which a rule was ever changed. A
deletion record that nothing answers for stays: the deletion record of an ordinary entry meets
the tag by chance about once in 17 million (§623 step 6), and a row can refuse in a store that
is whole until the operator sets it (§657). From then on the groups and entries are walked at
every hold, each hashed once for as long as the key is kept. What such a hold costs is measured
with the read in the plugin's own code, which §644 item 24 owes. *Held by* `StoreInForceTests`,
and by `StoreReadingCostTests` for the cost.

**A change of the API's reads the store once as well**, on KeePass's own thread, where every
other question reads afresh: from the lookup of its node to the change it hands the writer,
since nothing a route may change is a node or a string of the store (§633). The save that
follows is read afresh.

**No thread waits on a reading.** What is kept is used by a single reading at a time, which
takes it without waiting and puts the next in its place; a reading that finds it in use, which is
one beside a hold that lapsed with its reading still going, reads with nothing kept and keeps
nothing, and letting the database go at a lock marks what is kept to go and does not wait.

**What that rules out.** Nothing is kept by a modification time, since pykeepass 4.2.0 changes
a string and leaves the time (`measured-edited-refusals` 9.2). Nothing is kept until an event
of KeePass's announces a change, since an import raises no event and a synchronize is announced
only while KeePass's trigger system is on (`measured-desktop-windows` 6.3): a reading kept that
way would admit a revoked client until some event came. And no answer is kept at all, so a
revocation that is in the gate's copy when a hold begins holds in that hold: the margin is
none, and a request is never judged by an earlier reading.

**What it costs** (an estimate, run for this document outside the reports: KeePassLib 2.62 on
objects in memory, Windows PowerShell 5.1, a single machine, the median of 21 runs). The walk
is what grows with the database: 2.5 ms for 10,000 entries that hold no history, 13 ms where
each holds three history items, and 51 ms where each holds ten, KeePass's default limit. The
check of a store as the growth run left it after 1,000 writes (§643), 286 entries and 224,894
characters, took 0.9 ms with nothing kept and 0.06 ms where every version was kept, and a
summary entry at the plugin's own ceilings (§643) 6.8 ms. Holding 2,008 deletion records to the
tags took 0.02 ms. The hold `DESIGN.md` §119 allows is five seconds, and a listing of 10,000
entries holds the thread 22 to 26 ms today. So the reading stays within what a request already
holds the thread for, and a database in which every entry carries a full history is where it
shows. This estimate parsed no content and went through no router.

**What it costs through the product** (measured by `StoreReadingCostTests`, explicit, through the
real router and vault, in Release, the median of 21 runs; `FINDINGS.md` section 22). Over 10,000
entries in a hundred groups beside a store of 282 entries: the hold a connection is judged in,
which reads the store and nothing else, 3.5 ms; a read of an entry 3.5 ms; a listing of every
entry 39 ms. Where every entry holds ten history items, 21, 25 and 86 ms, and the worst run 148
ms. The first reading, with nothing kept, adds about 40 ms once. A request takes two holds that
read the store. A change's turn on KeePass's thread reads it once: 2.6 ms, and 21 ms where every
entry holds ten history items, where it read it at each question and took 6.3 ms. A
file that brings a store key of its own for each of its entries costs what any file of its size
costs (§649): 2 ms for a second reading of 300 such entries and 1 ms for 1,000, where the work
under each key took 0.7 and 19 seconds. With what is in force and the room worked out at every
reading as well (§623, §627), the same holds measured 2.4, 2.7 and 48 ms, which is the earlier run
within what two runs on one machine differ by; and a store at both of the plugin's own ceilings
(§643), a summary entry naming 7,900 summary entries each with its deletion record and restating
9,000 revocations, 900 of them with a deletion record besides, held the thread for 21 ms, and 24.6
ms in a later run. **With the admission of the certificate read by meaning in that hold** (§615), as
a connection is judged, asked of the gate's own target for the database: beside the same store, an
authority the product created and a single admission, the hold measured 2.6 ms, and 2.8 ms beside
the ten admissions the growth run's gate held (§643); where every entry holds ten history items,
20.6 and 20.7 ms, and the word alone 20.6 ms in the same series. The word alone measured 2.6 ms in
the same run as the first two, so reading the admission adds less than two runs differ by. **At a
summary entry restating 9,997 certificates taken away**, the most that leaves the store room for a
write, beside 10,000 entries without history, the hold measured 16.9 ms for a pairing's admission
beside those statements, and 27.2 ms for the renewal at the end of a chain of `from` as long as the
statements, each renewed from the certificate before it and naming the next (§648 rule 4): following
a chain as long as the store can hold costs about 10 ms over the statements alone. **With the names
read in that hold as well** (§616), beside the same store, a single admission
and 32 names on the list, the hold measured 2.78 ms at the median and 3.95 at worst, against
2.72 ms for the admission alone in the same run; where every entry holds ten history items,
21.7 ms against 20.6 in the same series. Beside a single name removed and added again 7,936 times,
the most that fit the addition's content, each removal an item of the summary entry and the addition
naming all those removals, it measured 8.85 ms, 14.7 at worst, and 27.3 ms with the history: the
names cost about 6 ms at that bound and nothing a run can tell at 32 names (`StoreReadingCostTests`,
explicit, in Release, the median of 21 after a first). **Beside the standing answers to a
renewal** (§618), with the admission's client among those answered, the hold measured 2.75 ms
beside no answer, 3.40 ms beside 100 clients each holding a standing answer and 9.53 ms beside
1,000, and 3.74 and 12.5 ms where each of those clients was answered ten times: about 7
microseconds a standing answer, with no text parsed in the hold (§643). So the estimate holds in its shape, the
walk somewhat dearer in the product, and reading every entry at every request stays well within
the five seconds a hold is let run, with a full history on every entry the case that shows. That
is a store before writes above have renewed much: every renewal ever made costs every later hold
for good, and §643 has the line a hold follows and the writes after which it passes a second and
five seconds.
*Held by*
`StoreReadingBoundTests`, `StoreReadingTests` and
`StoreInRealDatabaseTests`: a string replaced by an object of another text is read at the next
hold with the entry's times left as they were; a second question inside a hold walks nothing;
and nothing kept outlasts a lock.

**What is built.** The reading of this section, of §621 to §624, of §649 to §651 and of §658, whole
as far as it decides whether a store may be served: `StoreReading` over the candidates and the
deletion records `StoreSource` walks, read once for each hold through `IReadHold.Stamp`, with the
checked forms kept in `StoreKept` and let go, their keys overwritten, with the target at a lock and
a close. It answers the state of §656 and the guard's keys and tags (§633), and every entry
that is damage under the key that stands, with the kind its tag names (§621). What is in force
(§623) is `StoreInForce`, worked out for a store that is whole or stopped: every record under the
store key with whether a deletion record names it and whether it counts by its history, the
summary entries present, in force and told of by deletion records, every summary entry told of
that nothing in force replaces, every record no summary entry restates, what the retirements, the
revocations and the removals take away by record, by item and by deletion record, and what a
deletion record alone takes away, and the statements that took certificates away by record, by
item and by a deletion record of an admission key the file knows (steps 1, 2, 4 and 7), each
statement a deletion record alone makes held apart as well, and the objects a damaged version
would make refuse once deleted with a deletion record (§625); and the highest format number and
plugin version (step 6), the authorities that stand, and the room of §627. The records of kind
`profile` under the key are kept, in force or not, with the texts of §658, and the removals of
each name by their identifiers, a removal known as a record, an item and a deletion record counted
once: what the write of §627 step 3 deletes additions by. Which names are on the list (§616) is
worked out from those by `ProfileNames`, from the reading of each hold that asks, and kept
nowhere. The admission keys the file knows are those of step 2: the `certificate` and the `from`
of every record of kind `client` present under the key, in force or not, each where it is an
admission key, and what the records and the items that took certificates
away name; they are gathered only where a deletion record under the key carries the tag of a
certificate taken away, so a store whose deletion records carry no such tag hashes no subject for
them. The records of kind `client` are kept with the texts of §658, and their meaning is read
from the reading of each hold by `StoreAdmissions` (§615): the admission of the certificate on a
connection is parsed there and kept nowhere, and every admission is parsed only for a page or a
dialog that lists them. The authorities that stand under each key that stands are worked out for
every state but no key and a store past `StoreFormat.MaxKeys`, of which no key is judged, as a
word is answered under them (§656). What each kind means for access beyond that (steps 3 and 5)
is built by the pull requests that bring each kind's meaning, the authority's (§614), the
admission's (§615) and a profile name's (§616) among them. The authority a database is served
with is the single authority that stands in a store that is whole, read from the version that
passed, and every admission holds the target it is made on to what the store then reads: the
authority's entry, the version of it that passed, and that it still stands alone, so a retirement
written under a target already handed out refuses from that target's next admission on. A database
whose store holds no key is served nothing, and a gate that was serving it when the key went answers
`lost` from what it served with (§656). *Held by* `StoreReadingTests`, `StoreReadingStatesTests`,
`StoreReadingKeysTests`, `StoreReadingFormatTests`, `StoreReadingWholeTests`, `StoreInForceTests`,
`StoreRoomTests`, `StoreInRealDatabaseTests`, `StoreStatesInRealDatabaseTests`,
`StoreAuthoritiesTests` and `StoreAuthorityServedTests`, and in a real KeePass by `LiveStoreTests`
and `LiveAuthorityTests`; the admission keys by `StoreInForceTests`: a deletion record of a
certificate taken away tells of it by the `certificate` of an admission, in force or not, and by
the `from` of a renewal, as a statement of its own that the room counts; a text that is no
admission key adds no key; and a store with admissions hashes no subject for them until a
deletion record carries that tag; and the removals of a name, by record, by item and by deletion
record, a removal known all three ways counted once, and the additions, in force or not.

**What is built of the rules' reading.** `StoreInForce` holds the objects of every type with their
standing versions and the versions lost (§617), worked out as the reading is made. A deletion record
under the tag of `version` costs no hash where a version is present under it, and none where it
lies at the place of a version present, the first ten bytes of an identifier, which a version of
the same node, type and tag shares: so no subject is hashed and no tree walked for a store whose
deletion records under that tag each lie at such a place, which is every store the plugin's own
deletions and renewals leave, since the version that names a deletion record is at its place.
Whether a version of another object names a deletion record changes none of that, and what a
deletion record refuses is judged at its place, by `after` for every object there and by `lost`
for each object alone (§617). For a deletion record at no such place:
`rule:` and each node that
the file's objects name, and `switch:` with it where a deletion record left holds 0 in bytes 9 and
10; then, where such a deletion record holds 0, `renew:` and each client key the file knows
(§618); then, only where a deletion record is still not told of, the same for each group and entry of
the tree not yet tried, the recycle bin and the plugin's own group among them, walked once and only
while the reading is made; and a tag for each name the file knows once a deletion record meets a
node's rows. Each hash and each tag is kept with the key. **Every `after` and every `lost` is
read once**, with the checked form of the version that holds it, and kept with that form; a
deletion record is compared as its bytes and answered by a search of the lists in force of the
objects it tells of, those in `after` first and those in `lost`, which a version holds only where
it settles a loss, where no `after` names it, with no text made of it unless it is lost, so a
hold builds nothing as large as the names the lists hold and `lost` adds no pass over the
deletion records (§643). **The rules are worked out in the hold
that first asks for a rule**: `StoreReadings` keeps them beside the reading they were worked out
from, answers them for that very reading alone, and lets them go when the reading is replaced, at
the end of a turn and with the target. A gate's node keeps nothing and asks the rules of the hold it
is walked in. A read of an entry, a listing and a question about access each take a single hold and
a single reading; a change takes a single hold before its turn, and a reading in that hold and in
its turn; and the hold a connection is judged in works out no rule. A node the store holds no rule
for lets through what arrives, a database closed under a walk grants nothing, and a store that is
split or of a kind the plugin does not know grants nothing. **Not measured yet**, but for what
§643 has of an OK above many nodes and of a hold beside the versions it renews: what the rules
cost a listing and a write, and a hold beside a deletion record that nothing answers for, each
left to a later commit (§644 item 24). *Held by* `StoreInForceTests`, among them that a store
the plugin's writes left hashes nothing and walks no tree, and
`StoreInForceTests.A_deletion_record_named_only_by_a_version_of_another_object_tells_of_the_object_whose_place_it_holds`;
`StoreSourceNodesTests`;
`RuleWritesInRealDatabaseTests.A_reading_tells_of_a_rule_lost_at_a_node_no_object_of_the_file_names`;
and `RulesInTheHoldTests`, over the real router and vault, each count above and a rule narrowed
between two holds granting in the second what it was narrowed to.

### Which entries are records §621

**A candidate** is any entry of the database that carries a string whose name begins `KPG.`, in
its current version or in an item of its history. Its **versions** are its current strings and
then the items of its history, newest first.

**An entry of the plugin's that was stripped of all its fields is treated as a deleted record,
and not as damage** (a later decision; as measured). An entry with no `KPG.` string in any
version is no candidate, also where its identifier is an identifier of the store. Such an entry
is a record stripped of every string in a database that keeps no history, or by a program that
writes none. It reads as a record deleted with no deletion record: what took access away is
still restated by the summary entry, a stripped summary entry leaves none in force and stops
the store, and a stripped carrier of the store key leaves no key (reasoning). The other reading,
under which every entry whose identifier begins with a tag of the store is a candidate, would
take an ordinary entry for damage by the three bytes of a tag alone, which a random identifier
meets about once in 17 million entries for each kind and key (reasoning).

**An entry counts wherever it sits.** The reading takes every candidate of the whole tree: in the
plugin's group, in any other group, and in the recycle bin. So a record that a person moves, or
deletes into the bin, still counts, and such a move takes nothing away and gives nothing back.
The design paper counted only entries directly in the group; read that way, six replays on real
files ended wider, and with the recycle bin read for every kind five of them hold and the sixth
still ends wider (evaluation 4.1); the sixth is the sequence of §639 in which every deletion
record is removed. Reading the bin is what kept every deletion into the bin as decided
(`measured-store-as-a-whole` 3.4).

**The store keys are found first.** A version of kind `authority` or `retired` whose content
holds `storeKey` gives that key when the version passes under it: the check matches and the
entry's identifier is the identifier that content gives under that key (§609). A key is found
in such a version whether or not a deletion record names the entry (as measured). No other kind
of record and no other place gives a key. **A key that is found has only vouched for itself**:
which key is the store key, and what follows where several are found, is §649.

**A record** is a candidate with a version that passes under the store key. The first version
that passes, in the order above, is the record. Where that version is an item of the history,
the record **counts by its history** (§622).

**A record that passes the check and is of a kind the plugin does not know stops the gate for
that database** (a later decision). A version passes whatever its kind: the check is computed
over the kind as the entry states it, and the identifier is held to the tag of that kind and to
the six bytes of the check, under the store key. The subject and the height are not compared,
since what a kind is about is known only to a plugin that knows the kind (reasoning). An entry
that passes in that way is a record of this store that the plugin does not understand. The
plugin then serves nothing and writes nothing for that database, as an older plugin on a store
that was raised does (§652). The measured reading passed such a record over, which ends wider
where the record takes access away. This rule is a second safeguard beside the store's format
number: it needs no version record, so it holds where the format number was lost (§639). An
item of a kind the plugin does not know, in a summary entry that passes, stops the gate in the
same way (proposed), and so does a version whose object is of a type the plugin does not know
(§658). The kinds a plugin knows are the rows of §658.

**The store reads only its own records** (decision 23). A copy of a record fails the comparison
of §609 and is passed over wherever it sits. A copy grants nothing, takes nothing away, is no
source of the store key, is never written again by the plugin, and the tab lists no copy as
taking anything away. The API hides a copy all the same (§633).

**What that means for an import.** When a stale copy imports a newer file with Create new IDs,
the newer file's decisions arrive only as copies and are passed over. The stale copy is then
exactly as stale as it was before the import: nothing gets worse, and nothing is brought up to
date. Only Synchronize brings a copy up to date (§640). Two readings under which a copy would
count were measured and are not taken: under them a limit taken from a copy outlasted the
decision that replaced it, deleting the counted copies by hand gave the earlier answer back, and
a record written from a copy left a file with no store key (`measured-two-readings` 3;
`measured-copies-extended` 3 to 5). Both reports stay on the branch as measurements of readings
not taken.

**Damage** is a candidate with no version that passes whose identifier begins with the tag of
some kind the plugin knows, under the store key: an entry that sits under an identifier of the
store and is not what the plugin wrote.

**An entry that fails and is not the store's** is a candidate with no version that passes and no
such tag. It is passed over and shown on the tab, and is never damage. KeePass's importer brings
a stranger's entry with `KPG.` strings in unchanged (evaluation 4.1); that such an entry would
otherwise stop the gate is reasoning. An entry that passes under a key not kept is passed over
and shown in the same way (§649).

**Where no key is found**, nothing is recognized as the store's, and nothing is served (§632).

### A second store key §649

**Records under more than a single store key stop the gate** (a later decision). When records
under more than a single store key pass their check, the gate serves nothing for that database
until the operator keeps a store, as when two authorities stand. A file from outside adds no
record that counts.

**Against whom that last sentence holds.** It holds against somebody who does not hold the
database's own store key. Such a person can bring a key of their own and records under it, and
cannot make the database's key count for less, so two keys stand and the operator is asked. It
does not hold against somebody who holds the database's own key, who can write a record that
sets that key aside (below) as that person can write any record under it (§639).

**How the reading establishes it** (reasoning from that decision; none of it was run).

1. **A key is found** as §621 has it: in a version of an authority or of a retirement that holds
   `storeKey` and passes under that very key. That is all a key needs in order to be found, and
   it is all that somebody outside needs: anybody can make an entry that holds a key of their
   own choosing and passes under it. Nothing in the file tells which key is the database's own,
   since the file holds no memory of itself and nothing is kept outside it (decision 2).
2. **Keys are compared by their value.** An authority's entry and a retirement that hold the
   same key give a single key.
3. **A keep record** is what the operator's choice writes (below): a record under the key kept
   that names a key not kept, and that names in `after` every keep record its writer had read
   (§610). A choice writes a keep record for each key not kept. A keep record that another keep
   record names in `after` is replaced and does not count, under whichever key each of the two
   is. So a later answer replaces an earlier.
4. **A key is set aside** when a keep record that counts names it, under a key that is not set
   aside itself. That is worked out from the keys that no keep record names: they stand, the
   keys their keep records name are set aside, the keep records under a key set aside are
   dropped, and so on until nothing changes. **Keys whose keep records name each other in a
   circle set none of them aside**: all of them stand.
5. **A single standing key is the store key**: every record that counts, counts under it, and an
   entry that passes under any other key is passed over and shown. **Several standing keys
   serve nothing** until the operator keeps a store. Wherever a key is found, a key stands.

**Where this departs from the measured code, and why.** `probe/read.py` holds every key it finds
and counts whatever passes under any of them: an authority, a retirement, a rule, a summary
entry. No run gave that reading a file that brings a key of its own (§608). Read from that code
and not run, such a file ends wider. A file from outside holds a retirement that carries a key
of its own and names no authority of the database, a rule version under that key for a node
with no rule of its own, whose identifier any client that may read there knows, and a summary
entry under that key that restates both. Brought in by Synchronize or by an import that keeps
identifiers, it leaves the store whole and the database's authority served, and the node grants
what the outsider wrote. With an authority of its own and a retirement that names the
database's authority, the outsider's authority alone stands and is served. Under this section
each of the two ends with nothing served.

**What the operator is asked** (proposed). KeePass asks which store to keep in a dialog over its
main window, as it asks about two authorities, `CA.md` §204, and never from a connection; the
KPG tab asks again. For each key that stands the dialog shows what lets the store under it be
recognized: its authority with its fingerprint and when it was made, the clients it admits, its
profile names and how many rules it holds. Until the operator has answered, a connection is
answered as §656 has it.

**What keeping a store writes** (proposed). A single write under the key kept, through the
writer: a keep record for each key not kept, and the summary entry, which restates each. The
keep record names a key by a digest of that key, which nobody computes who has not held that
key, so a file from somebody who does not hold the database's key cannot bring a record that
names it. A keep record counts wherever it sits, as an entry, as an item of any summary entry
present and as a deletion record that tells of it. A plugin keeps only a store that it may
write (§652), and no other write is made while several keys stand.

**The form of a keep record** (S23, §658). Its kind is `kept`. Its content holds `key`, the
name of the key not kept, and `after`. **The name of a store key** is the HMAC-SHA256 under
that key over the three characters `key`, as 64 lowercase hexadecimal characters. That text
holds no line feed, and every other text the store puts under a key holds a line feed (§608,
§609), so a name is no tag, no subject hash and no check. The subject in the identifier is
`key:` and that name. **A keep record names a single key**, and not each key not kept, because
a deletion record is an identifier and nothing more: with a single key for a subject, the
deletion record of a keep record still tells which key was set aside and under which key
(§623), and with several it could tell neither. Its item in the summary entry holds its
identifier, the name and its `after` (§611). The plugin never deletes a keep record. A store
started again is no kind of its own: the command writes keep records under the new key (§632).
*Held by* `StoreReadingTests`: a key that a keep record, its item or its deletion record names
is set aside; a keep record that a later keep record names in `after` sets nothing aside; keys
whose keep records name each other all stand; and a keep record under a key that is set aside
sets nothing aside.

**Keeping a store deletes nothing.** The entries under a key not kept stay in the file. An
earlier form of this section deleted them with deletion records, and that form loses both
stores: where two copies each keep another store and are then synchronized, each copy's
deletion records delete the store the other copy kept (reasoning). Whether those entries are
ever removed, and by what, is S30.

**What becomes of the records under a key not kept.** They count for nothing from that write on,
in that copy and in every copy the keep record reaches, and they are passed over and shown. The
API hides them (§633). Nothing of them is carried over: a client admitted under that key pairs
again, and a rule written under it is entered again. The operator can keep the other store
later, with a keep record that names the earlier keep record in `after`.

**Copies that keep different stores are asked again.** Each copy's keep record names the other
copy's key, and neither names the other record in `after`, since neither writer had read it.
Once the copies meet, the two keys name each other in a circle, both stand, nothing is served,
and the operator is asked again. The answer names both earlier keep records in `after` and
settles the question in every copy it reaches. Nothing is lost by it. The dialog still warns to
answer on a single copy and to synchronize before answering on another, as the authority's
chooser does, `CA.md` §204.

**How two keys come about with nobody attacking** (reasoning). Two copies each make a first
authority before they meet. Or a store is started again after its key was lost, and a stale copy
then brings the entry that carried the lost key back, since an entry that a merge brings back
gives its key again (§632). The operator then chooses between the store started again and the
store that was lost. A store started again by the command of §632 comes about with nobody being
asked: the command sets every other key aside in the write that makes the new key.

**The reading bounds its work in the keys it finds** (this document's own). A key is found by
passing under itself, so a file anybody can make brings a key for each of its entries, and the
work the reading does under each key, a check of every version under it and a look at every pair
of keys, grows with their product. Past 32 keys found, `StoreFormat.MaxKeys`, the reading does no
work under a key that is not a version's own: the store is split, nothing of it is judged, and
the API's guard leaves out every entry that carries a string of the store, besides every entry
under a tag of any key found, which is what reaches a record stripped of every string of the store
outside the plugin's group. A key's tags are made once and kept with the checked form that gave
the key, as the key is, so a hold past the ceiling costs the walk and a few numbers for each key,
and no digest. A key also keeps the hash of each subject it was asked for, with the subject's
text, §620; under several keys that stand, only the `id` of an authority present is asked for.
A database the operator keeps holds a handful of keys over its life, one for each store started
again and for each pair of copies that made a first store apart, so the ceiling is several times
what a real database reaches. Within it, the marking of the keys is over their pairs, the
deletion records are sorted by their tags once for every key, and every record, item and
deletion record is looked at once. *Held by*
`StoreReadingBoundTests`: at the ceiling every key is judged, past it none is and the store is
split with the guard hiding more, and a file of 1,000 such entries is read again in milliseconds;
explicitly, the second reading of 20,000 such entries, 35 ms, is held to that of 20,000 entries
that give no key, 14 ms.

**What the operator does past the ceiling** is not what ends an ordinary split. Keeping a store
judges the keys, and past the ceiling none is judged, so no keep record can be read until the
entries that brought the keys are gone: the operator deletes them, which the log names by how
many keys were found and how many entries are hidden, and the store is judged again at the next
reading once the keys found are back within the ceiling.

**What it costs.**

- **A file that the operator merges stops the gate** until the operator has chosen, which fails
  closed.
- **An operator who keeps the wrong store** serves that store's authority and admits its
  clients, which is fail-open on a wrong answer. What the dialog shows is what stands between a
  merged file and the database, and a later answer takes the wrong answer back (§639).
- **Somebody who holds the database's own key is asked nothing.** That person can make a file
  that holds a store under a key of their own with a keep record that names the database's key.
  Once it is merged in, their key alone stands and their authority is served, with no dialog.
  It is fail-open, by the person who can equally write any record under the database's own key
  (§639).

**What is built.** The reading of this section: the keys found, those that stand and those set
aside, marked as step 4 has it, circles found in a single round all standing, and every keep record
read under any key found, as a record, as an item of a summary entry present and as a deletion
record that tells of a keep record, also where a single key is found, where the keep records mark
nothing and are what a store started again names in `after` (§632). Damage is judged under the key
that stands alone, so an entry of a key set aside that no version passes stops nothing. A keep
record under the store key that names a key found nowhere and that no summary entry restates is the
stop a store started again writes (§632): it stops the store as a record no summary entry restates,
also where no summary entry is present, and the listing of a stopped store names it apart with the
key it names, for the seal's dialog (§625). *Held by* `StoreReadingKeysTests` against the stores of
`StoreMeaningVectors.json` whose names begin `keys:`, as `probe/read.py` read them
(`measured-started-again` 8, 13): the state, the authority served, the keys found, standing and set
aside, every keep record read, and the stops listed apart. The measured reading names the profile
names of a split store from its records under every key; the product reads nothing in force there
and names none, which serves nothing either way.

### The history rule §622

**The rule** (decision 7). Every record carries the check. A record counts in its current version
when that version passes, or else in the newest version in the entry's history that passes. An
entry of the store with no passing version anywhere is damage, and damage stops the gate for the
whole database, as a missing summary entry does (§624).

**What it is for.** A person who changes a character of a revocation in KeePass's entry dialog
makes a newer version of that entry, and the newer version wins KeePass's own Synchronize. A
reading that ignored the edited entry would admit the revoked client, with no deletion anywhere.
A reading that stopped the database at once would refuse every client for a single accidental
edit. KeePass's entry dialog and its quick edits put the whole version before an edit into the
entry's history (`measured-hide-and-lock` 4.3), so the version the plugin wrote is still in the
file, and still passes.

**Where it holds.**

- **On a store without summary entries, no cell of 3,960 ended wider** after any edit that leaves
  the entry in the file: a character of the content changed, the content string renamed or
  removed, every string emptied, repeated edits, an edit in both copies, an edit by a program
  that writes no history, a history item changed by a script. Every cell that is not unchanged is
  a refusal (`measured-edited-refusals` 9). In that measurement a retirement passed under the
  store key of the authority it names, and every other record under the key of the authority
  served (`measured-edited-refusals` 2, 9.1). That is a check tied to an authority, and not the
  check of §621: the figure is of what an edit does to a record, and of nothing about which keys
  count (§649).
- **On the store as decided, no cell of 590 ended wider**: ten kinds of entry, each changed by ten
  operations as KeePass's own commands make them, then read, merged with an unedited copy by
  KeePassLib's Synchronize and by KeePassXC 2.8.0 in both directions, and read again after the
  gate's next write. 390 ended as decided, 28 narrower and 172 refusing
  (`measured-decided-store` 3.2).
- **Every kind read as decided while the written version was in the history**: after a single
  edit in the entry dialog, after the content string was deleted there, and after ten edits in
  all. A move, the recycle bin and a quick edit alone left every kind as decided.
- **An entry whose content nobody changed passes in its current version** however often it is
  edited otherwise: measured for a quick edit and a move, and reasoning from what the check covers
  for other edits.

**The stop is what makes the rule safe.** A reading that takes a record from the entry's history
and never stops for a failing entry ended wider in 798 of 2,160, 42 of 216, 42 of 432 and 33 of
144 cells of four parts of the same measurement (`measured-edited-refusals` 9.3).

**Where it stops holding**, and the whole database stops instead, each measured
(`measured-edited-refusals` 9.4, 9.5, 9.7; `measured-decided-store` 3.3):

- **Ten further edits under KeePass's default history limit.** The limit is 10 items. An edit
  that changes the content the check covers, followed by ten more edits of the same entry of any
  kind, quick edits among them, takes the written version out of the history. This document calls
  that the 11th edit. The written version leaves at the 2nd edit under a limit of a single item.
  A write of the plugin's own between the edits sets the entry back, and the count starts again
  (§628).
- **A database that keeps no history.** The first edit of a record's content stops the database.
- **Database Maintenance's "Delete history entries older than (days)"**, which removes the
  written version when it is older than the number of days, with no question. This was run on a
  store without summary entries, through KeePassLib and not through the window's command. On
  the store as decided it was not run (§644).
- **A program that writes no history.** pykeepass 4.2.0 changed the string, wrote no history item
  and kept the entry's modification time.
- **A history item changed or removed by a script.**
- **A merge with an unedited copy brings the written version back in some cells, and which
  cells turns on the second the edits were made in.** On the store as decided, a Synchronize
  with an unedited copy brought it back in no cell past the limit: KeePassLib trims the merged
  history to the limit, and KeePassXC 2.8.0 kept the newer entry whole. Through the product,
  with a record of kind `withdrawn` that both copies hold, edited past the limit in a copy, and
  the copies merged by `PwDatabase.MergeIn`, measured by method and direction
  (`StoreAdmissionMergeTests.A_record_both_copies_hold_edited_in_a_copy_counts_by_the_version_written_or_stops_the_store_past_the_limit`):

  | Merged into | The edits | Leaves that store whole, the record counting by the version written | Leaves it stopped |
  | --- | --- | --- | --- |
  | the copy that edited the record | each a second or more after the version before it | Overwrite existing | Synchronize, Overwrite if newer, Keep existing |
  | the copy that edited the record | within the second the record was written | Synchronize, Overwrite if newer, Overwrite existing | Keep existing |
  | the other copy | either | Keep existing | Synchronize, Overwrite if newer, Overwrite existing |

  KeePass compares modification times to the second, in a merge and at the history limit.
  Edited a second apart, as edits by a person are, only a merge that writes the other copy's
  entry over the edited entry brings the written version back to the copy that edited it.
  Edited within the second the record was written, as a program can edit, the two versions
  compare as neither older nor newer, and Synchronize and Overwrite if newer keep the version
  written as well. The other copy is stopped by every merge that puts the edited entry in the
  place of its own. What differs between the cells is which store stops: in each of them every
  certificate was refused where §648 refuses it.

**The two entries that carry the store key are the exception.** For the authority's entry, and
for the retirement that holds the store key, no passing version is no stop of this kind: the
file then holds no store key, nothing is recognized as the store's, and sealing the store again
does not apply. That is a store started again, §632.

**A database that keeps little or no history** (decision 20). With the database's history limit
at no item the rule softens nothing, and nothing can be written again. The limit, and the age at
which Database Maintenance cuts, are the operator's own settings, and the store relies on both.
The KPG tab states this for a database whose settings keep little or no history (§636), and the
published list names it as a condition (§637). The plugin refuses nothing because of it and
leaves the settings alone.

**What the rule does not answer.** An entry that is gone has no history. A record deleted with
its deletion record ended wider in 30 of 2,880 cells on a store built without summary entries
(`measured-edited-refusals` 5.6, 7, 9.3). The summary entry is the answer to that: on the store
as decided, a revocation, a retirement, a removal, a rule version or a renewal answer deleted for
good with a deletion record read as decided in every cell (`measured-decided-store` 3.2, 3.3).
Both carriers deleted is §639.

### What is in force §623

From the records, the deletion records and the summary entries under the single store key that
stands (§649). The steps are as measured for the kinds that were measured; for a certificate
taken away, a keep record and a version record they are proposed.

1. **A deletion record takes out of force** an authority, an admission, a profile addition, a
   version or a summary entry that is present beside a deletion record naming it. The plugin's
   own deletions leave such records, so a replaced record that a KeePassXC merge brings back
   stays out. A revocation, a retirement and a removal count whether or not a deletion record
   names them, and so do a certificate taken away (§648), a keep record (§649) and a version
   record (§650).
2. **What takes access away is a union.** The revocations, the retirements and the removals in force
   are those present as records; those any summary entry present restates, whether or not that
   summary entry is in force; and those a deletion record tells of. A deletion record tells of a
   revocation, a retirement or a removal when its identifier begins with that kind's tag under the
   store key and carries the subject hash of a client key, an authority or a name the file knows
   (§609). So deleting a revocation for good, in any program that leaves a deletion record, changes
   nothing. An item with no subject tells of its decision as such a deletion record does, by the
   keyed tag and subject hash it holds (§611). A certificate taken away is united the same way: it
   is refused where a record, an item of any summary entry present or a deletion record states that
   it was taken away. A deletion record tells of a certificate taken away when its identifier begins
   with that kind's tag under the store key and carries the subject hash of `certificate:` and an
   admission key the file knows. The file knows an admission key from the `certificate` and the
   `from` of every record of kind `client` present, in force or not and whether or not it reads as
   an admission (§615), since a record beside a deletion record still holds its content in the file,
   and from the `certificate`, the `from` and each key in `renewed` of a record or an item that took
   a certificate away; a text that is not 64 lowercase hexadecimal characters is no admission key.
   Knowing a key only lets a deletion record refuse. The `from` of a renewal is among them because
   the admission of a certificate taken away is deleted in the write that takes it away, so where
   the record and its item are then lost, a renewal's `from` is the text left that names that
   certificate, and with it the deletion record still refuses that renewal. It rules out knowing a
   key from `der`, since `certificate` tells also for a record that does not read as an admission,
   and from an admission's identifier, whose subject is the client key. Such a deletion record whose
   identifier is that of a record present, or the `id` of an item, adds nothing to what that record
   or item states. Any other stands for a record whose content is nowhere, and is a statement of its
   own that names no renewal (§648). A keep record is united under the rules of §649.
3. **The versions of an object** are the records of kind `version` in force and the versions
   that the summary entries in force restate. Those that no version names stand, and the
   object's value is what all standing versions allow (§617). An object refuses as §617 states,
   which has what a deletion record tells of a version of a rule and of a switch (S8); §618 has
   what it tells of an answer to a renewal, and what the versions of an answer give.
4. **A summary entry is in force** when it is a record and no deletion record names it.
5. **The authority served, the names on the list, the certificates admitted, each node's rule and
   each answer to a renewal** follow as §614 to §618 and §648 state.
6. **The highest format number and the highest plugin version** are the greatest that a version
   record that counts, a summary entry present, or a deletion record that tells of a version
   record states (proposed, §650). A deletion record tells of a version record when its
   identifier begins with that kind's tag under the store key, holds a number from 1 in bytes 9
   and 10, and holds in bytes 4 to 8 the subject hash of `format:` and that number (S23). It
   then states that number as a format number, whether or not the plugin knows a format of that
   number, and it states no plugin version. So eight keyed bytes tell, and never the three of
   the tag alone: the deletion record of an ordinary entry meets them by chance about once in
   2^64, where the tag alone is met about once in 17 million and would hold a plugin back for
   good. What the plugin does with the store follows from the comparison of §651, before
   anything is served.
7. **A deletion record tells of a summary entry** when its identifier begins with that kind's
   tag under the store key and holds in bytes 4 to 8 the subject hash of `store`, and bytes 9
   and 10 are then its height (S23). The measured code held such a deletion record to the tag
   alone, which the deletion record of an ordinary entry meets by chance, and the store then
   stops for a summary entry that never was (§624). Every summary entry carries that subject
   hash, so no deletion record of a summary entry is missed. **A deletion record tells of a
   keep record** when its identifier begins with that kind's tag under a key found and holds
   the subject hash of `key:` and the name of another key found. It counts as a keep record
   under the first key that names the second and names nothing in `after`, and it is replaced
   where a keep record that counts names its identifier (§649).

No step reads a time, the place of a record, or which group is the recycle bin.

### When the whole database stops §624

Where a single store key stands, the store is whole when all of these hold, and otherwise it is
stopped. They are as measured; the design paper 5.2 has the same conditions with a plain stop
for any failing entry where the history rule now stands.

- **A summary entry is in force**, where the file holds any record or any deletion record that
  tells of a summary entry.
- **Every deletion record that tells of a summary entry** names a summary entry in force or an
  entry that a summary entry in force names in `after`. The plugin deletes a summary entry only
  in the write that replaces it, so its own deletion records always pass. A record that does not
  pass means a summary entry was deleted that nothing present replaces.
- **Nothing is damage** (§621), which under the history rule is an entry of the store with no
  version that passes.
- **Every decision present is restated.** Every revocation, retirement and removal present as a
  record is restated by some summary entry present, and every record of kind `version` in force
  that no version names is carried by some summary entry present. A decision without a summary
  entry that restates it means that summary entry was lost on the way. A certificate taken
  away, a keep record and a version record are held to the same (proposed), each by the item
  that §658 names for its kind. A version record is restated by any summary entry present that
  states a format number at least its own, so a version record that states nothing higher than
  the store already states stops nothing.

**No condition asks whether the store states a format number.** Every summary entry states it
(§650), and a summary entry that states none does not read as a summary entry, so a store that
is whole states its format.

**A stopped store serves nothing**: no client is admitted and no rule is read, in that copy and
in every copy the deletion or the edit reaches. What a connection is answered is §656.

**A stopped store is written to by no command but the press that seals it again, and Create is
no exception** (this document's own). The measured code differs: its Create asks only whether an
authority stands, and not whether the store is whole, so on a stopped store with no authority
standing it deletes what is damage and writes a summary entry over the rest. That is a store
sealed again without the page of §625, and without the versions granting nothing that the press
writes for what was lost. So Create is refused on a stopped store while a store key stands, and
the operator seals the store again first. Where no key stands there is no stop, and Create
starts a store (§632). Run in the model with the seal on (`papers/ground/measured-seal.md`, R15
and R17): Create refused before the seal, and taken after it, alone or in the seal's own write.
Built: Create alone is refused on a stopped store at `AuthorityService` and in the plan's first
step, and planned over the seal where the seal is the first act of the write.

**Other states serve nothing and are not this stop**, and the press does not apply to them: no
store key found (§632), several store keys (§649), and a plugin that is held back, by a store
raised above it or by a record of a kind it does not know (§652, §621).

**A store with no room for its next write is answered as a stopped store, and the press does
not lift it** (a later decision, §643). The gate serves nothing for that database and answers
`stopped`, until the store is started again (§632). It is no condition of the list above: such
a store can be whole, and what it lacks is room. The reading judges it at every hold, after
every condition above, so a store that is stopped for another reason as well is named for that
reason, and the log gives the room as the stop's reason (§643). *Held by* `StoreRoomTests` and
`StoreInRealDatabaseTests`.

**What lifts it** is sealing the store again (§625). No merge lifted a stop in the measured
cells. A merge with an unedited copy lifted a stop by an edit in no cell past the history limit.
After a summary entry was deleted with its deletion record, Synchronize left both copies
refusing, and a KeePassXC merge into the unedited copy, which carries no deletion record, left
that copy as decided (evaluation 6, row 17).

### Sealing the store again §625

**"Seal again" is the single press for a stopped store**, on the KPG tab. Like everything on that
tab it is applied at the settings dialog's OK (the design paper 5.4).

**What it writes**, as measured in `model/crdt_model.py`: for every object that refuses because a
deletion record tells of a version whose text is nowhere, a version that grants nothing, naming in
`after` the versions of that object in force and what they name there, and in `lost` what was
lost and what those versions name there (§617); then a write as §627 has it, which deletes
every entry that is damage with a deletion record and adds a summary entry over everything that
passes and is in force. The
press writes no version below anew: the row or the switch it writes for refused already while
its version was lost, so nothing that arrives below changes with the press (§617, as run).

**What its page has to state.** Whatever is not on the page when the operator confirms is not in
the new summary entry, so the press makes a loss permanent in that copy. From the deletion
records the page can show that a revocation, a removal or a version of a certain rule was
deleted, since an identifier holds its kind and a keyed hash of its subject, and for those the
press writes the refusal down. It cannot show a decision lost in both of its carriers with no
deletion record, as a script, a merge that keeps no record or a file written over leaves it. The
page states plainly that it cannot know what was deleted without a record and that confirming
makes the shown state the state, and the tab lists what is in force, so that it can be checked
first (§636).

**What the reading lists of a stopped store** (built; as measured in
`papers/ground/measured-seal.md` on `spikes/storage-survival`, which listed it with `probe/read.py`
before anything of it was built). Every condition of §624 the store fails, and its room (§643), and
not the first alone, which is all the reading's reason names. Every entry that is damage, by its
identifier, the kind its tag names under the store key and its title. Every summary entry a deletion
record tells of that nothing in force names, where a summary entry is in force; where none is, that
is the condition, and the listing names those a deletion record tells of at the greatest height of
any summary entry of the store, present, damaged or told of, where `probe/read.py` names none: every
write deletes the summary entry before it with a deletion record, so the listing names the summary
entry deleted last, the summary entry the stop is about, and none where a summary entry present or
damaged stands above it. That is what the dialog shows alone: the seal's summary entry names every
summary entry told of as replaced either way, so nothing the seal writes depends on it, and the
tests that hold the listing to the measured reading's hold the product's own rule there, which no
vector states. Every record present that no summary entry present restates, a condition only where a
summary entry is present: both what restates and whether it is a condition are asked of the summary
entries present, in force or not, and the condition of no summary entry in force of the summary
entries in force, as `probe/read.py` keys them, so a summary entry present beside its own deletion
record makes both conditions hold. The revocations, the retirements, the removals and the
certificates taken away that a deletion record alone states, which the union of §623 step 2 keeps in
force and the seal's summary entry restates; and apart, the revocations, the retirements and the
removals of names that deletion records alone tell of whose subject the file names nowhere, each
with every deletion record that tells of it, which the seal's summary entry restates by an item with
no subject (§611). Every object a lost version makes refuse, with the deletion records that tell of
it, and what the seal's version of it names: in `after` every version of the object in force and
what those name there, and in `lost` what was lost and what those name there, no identifier in both
lists. And every object the deletion record of a damaged version would make refuse once the seal has
deleted that version with a deletion record, and that no lost version makes refuse now. Every stop a
store started again wrote under the store key, with the key it names (below). A deletion record of a
version at a node the file does not know tells of nothing (§617) and is listed nowhere. That is what
the page can show, by kind; a decision lost with no deletion record it cannot show. The listing is a
function of the reading and hashes nothing beyond it: a damaged version is told of at its place in
the reading's own pass over the deletion records, so the tree is walked once at most. *Held by*
`StoreLossesTests`, against every store of `tests/Shared/StoreMeaningVectors.json` whose name begins
`stopped:`, read beside the tree of the stores of rules, which is the tree their answer names and
which the measured reading read them beside; and the certificates taken away, which neither the
model nor `probe/read.py` holds, by `StoreLossesTests` alone; and by `StoreInForceTests` and
`StoreReadingStatesTests`.

**What was run.** The press left the store as decided wherever it was run after a summary entry was
deleted or deduplicated (`measured-store-as-a-whole` 3.4, 3.6). With a record for each node and
profile, the press wrote an empty rule for the row whose version was lost and for no other row, and
the switch refusing for a lost version of the switch (`measured-rules-per-profile` 8). As a page in
a real KeePass it is run by the end to end tier's `LiveSealTests` (§644 item 13). Pressed after a
loss that left no trace, it ends wider (§639). **Run before it was built**
(`papers/ground/measured-seal.md` on `spikes/storage-survival`), in the model and on real files, as
the design below has it: after a stop by each kind of entry edited past the history limit or with no
history kept, the seal applied wherever the key was held and left the store whole, what the damaged
entry held standing where a summary entry carried its text and refusing where none did; R1 to R21
and R3s, each kind of loss, the seal beside an act in a single write and the seal composed after a
merge under its dialog, merged by every method both ways, with no file beyond its own ideal outside
the classes §639 accepts; and random and exhaustive histories of the seal's acts, in which no seal
left its store stopped and no object that refused for a lost version granted after it.

**Where it does not apply.** Where the file holds no store key there is no stop to lift (§632).
It lifts neither several store keys (§649) nor a plugin that is held back (§652). Sealing is a
write, so a plugin that is held back, or that waits for the store to be raised, cannot seal. **Nor
where its write would leave no entry holding the store key** (as measured, `measured-seal` 8 item
2): where the entry that holds the key is damage and only an entry the write prunes holds it, as
a retired authority's entry does after Delete with the retirement edited, or after Recreate with
the new authority edited, the plan refuses the seal as it refuses every such write, and the press
is not offered. The way out there is the command that starts the store again (§632).

**What is built.** The seal is an act of the store, `SealActs`, the only act a stopped store takes,
and only as the first act of its write (`StoreAct.Seals`); it is refused where the store has no
room, which the seal does not lift, wherever the plan refuses it, and where its own write would
leave the store stopped, as a summary entry naming as many as a string holds leaves no room for the
write after it (§643): asked of the store as the write would leave it, before the act is written or
held, so that a press refused holds nothing for OK. **Its versions** are those of the listing above,
each composed by its type's own text, an empty rule for a profile's row, the switch refusing for a
switch and `refuse:forever` for an answer, naming in `after` every version of its object in force
and what those name there and in `lost` what was lost and what those name there, no identifier in
both; a version whose content or item does not fit the ceiling of a string is left out and named,
and its object goes on refusing; an object of no type the plugin writes gets none. Nothing is
written for what a deletion record alone keeps in force: the write's summary entry restates it.
**Its write** deletes every entry that is damage, whatever its tag, each with a deletion record, and
names in its summary entry's `after` every damaged summary entry it deletes, beside everything a
summary entry names (§611), above whose heights it stands; without that name its own deletion record
would stop the store again at once (as measured, `measured-seal` 8 item 1). The acts after it in the
same write are planned over the sealed store, Create among them. **It is composed at the write**: an
act that holds what is to be done, composed again wherever the KPG tab's buffer is read and at OK
over the store as it stands then (`StoreAct.Again`), so a merge under its dialog that brought a lost
version's text back leaves that object out, a further loss has its version, and over a store that
became whole the act comes to nothing. **A decision whose subject the file names nowhere** (as
measured, `measured-seal-restates`): where a revocation, a retirement or a removal is deleted with
its deletion record and with every summary entry, and no record or item left names its subject, the
seal's summary entry restates it by an item with no subject (§611), and every write after it carries
that item until the subject is named. A copy from before the decision that brings the subject back,
by any merge into either copy and with the sealed copy's deletion records kept or cleared, finds the
client refused, the name off the list and the authority retired, as the measured model's seal leaves
them; restated by the subject alone, as the seal first did, the deletion record was the decision's
single carrier (§639). *Held by* `SealActsTests`, against the seal's records of
`tests/Shared/StoreRecordVectors.json` and the `seal` of every store of the `stopped:` family;
`StoreWriteTests`, `StoreRoomTests` and `AuthorityCommandsInStoreTests`; and `StoreWriteShapeTests`,
R1 to R21 and R3s of `tests/Shared/StoreWriteVectors.json`, each played through the product's acts
with the seal pressed where its dialog is shown and written at the step that seals through the KPG
tab's buffer, held after every step by shape and by answer, and R21 as the accepted class of §639
row 5; and R5 to R7 and F5 to F7r of what the seal restates, each followed by a merge by Overwrite
if newer and by KeePassXC into the other copy as sealed, and by those and by Synchronize with the
sealed copy's deletion records cleared, each summary entry's items with no subject held to the
model's by the subject each is about. The sequences are played over the unit tier's store, whose
merges are not KeePass's where both copies hold an entry apart: its Synchronize takes this copy's
entry where KeePass takes the entry modified last, and the sequences put the other copy's entry in
its place where they edited it there; no sequence passes such an entry where the real merge would
end wider, and the real merges are the host tier's. Over real databases, `SealInRealDatabaseTests`
seals each stop on the tab's buffer and writes it at OK through the real writer: the only summary
entry deleted, a summary entry made apart deleted beside one in force, each kind of entry edited
past the history limit, and a record no summary entry restates; `StoreWritesUnderDialogsTests`
refuses it under each window that would take it back, and `StoreSealMergeTests` holds it through
each of KeePass's merges in both directions, the copy merged into whole in every cell, F5 among them
with the deletion records kept and cleared.

**Over a store a start stopped** (as measured, `measured-started-again` 5). A store started again
writes, under each key it sets aside, a stop: a keep record naming the new key that no summary entry
restates (§632). Once the new key is lost, the earlier key stands alone and the stop stops its
store. The listing names each such stop apart, with the key it names, which is found nowhere, and
the seal keeps the store over it: for each stop, a keep record under the store key naming the key
the stop names, with the stop in `after`, which the write's summary entry restates beside the stop.
The earlier store is served again with its own key, its authority and its clients, and what was
decided under the new key is out of it. Should the new key come back by a merge that carries no
deletion record, the two keep records name each other's keys in a circle, both keys stand and the
store is split until it is started again: keeping a store of several (§649) is not built. A split
store whose new key is lost is split again, each earlier key holding a stop, and the seal does not
apply to it; a further start settles it. *Held by* `SealActsTests`, against the keep record of
`tests/Shared/StoreRecordVectors.json` the seal writes over the store of the `keys:` family whose
new key was lost, and `SealFormTests`, the row of a store set aside.

**What the page and its dialog are.** Over a stopped store the Certificate Authority page states
why, and where the seal could be written and lift the stop offers **Seal store again** in Recreate's
place; where it could not, for want of room, because its write would leave no entry holding the
store key, or because its write would leave no room for the write after it, it offers no seal and
states why, and offers Start store again wherever the store takes a start (§632). The press asks the
same of the store as it stands at the press, and opens no dialog over a seal OK could not write. The
dialog is the plugin's own, sized as the authority chooser is: the reason the reading names first,
and a row for each loss the listing shows: each stop a start wrote, each summary entry deleted that
nothing replaces, each object a lost version makes refuse, each decision a deletion record alone
keeps in force, each damaged entry and each object its deletion record would leave refusing, with
its kind, what it was about, and what the seal makes of it, every deletion record that tells of it
in the row's tooltip. A rule's node is named by its title wherever the tree holds it, the recycle
bin among it, and an answer to a renewal by the name of its client where an admission names the
client; a decision whose subject the database names nowhere is named by its kind alone. A record no
summary entry restates has no row, the seal's summary entry restating it, and no condition has a row
of its own: with no row, the dialog states that no deletion record shows a loss. Under the list the
two sentences on what cannot be shown, and a count of what the seal keeps, the clients as the
Clients page counts them. Confirming holds the act in the tab's buffer, and OK writes it, logs what
it wrote and deleted, and writes the audit's line. While it waits no code is handed out, and the
line over the invite button names the seal: to pair once OK has landed it where the sealed store
holds an authority, and that it takes effect at OK where the store holds none, since the seal
deletes no authority. **What the page's question costs** (measured): whether the seal could be
written and lift the stop is asked at every reading the page draws a stopped store from, the seal
composed, planned, applied in memory and the store it leaves read. Beside ten thousand entries and a
thousand lost objects it took 77 ms at the median where the page's reading took 64 and the press up
to the dialog 184, and with ten history items on every entry 73, 87 and 184 ms (`FINDINGS.md`,
`StoreReadingCostTests`). *Held by* `SealFormTests`, `AuthorityDescriptionTests`,
`AuthorityPanelTests`, `PendingRecordsTests`, `GateWiringTests` and `GateTabFitsTests`.

---

## 5. Writing §626

**The single writer and its save are today's.** Every write to the store goes through
`DatabaseWriter`, in its turn on KeePass's thread, is saved through `MainForm.SaveDatabase`, and
is believed only once `PwDatabase.Modified` is clear and the file hashes back, with
`500 not saved` and `500 not verified` meaning what they mean today. `DESIGN.md` §119 holds for
all of that, `API.md` §408 for what a client is told, and `DESIGN.md` §120 for the audit lines
the writer writes on its own landed path. No certificate is handed over before its admission and
the summary entry written with it are in the file, which is the order `PAIRING.md` §307 keeps.

**Which of KeePass's windows a write to the store is refused under has to be asked again.**
`DESIGN.md` §124 lists the windows the plugin watches and why, and several of those reasons are
about custom data, which the store no longer uses: Database Maintenance's copy of the database's
custom data, and a group dialog's copy of its group's. The store's records are entries, so the
question becomes which window puts back or takes away an entry of the plugin's group, which is
measured (§644 item 14).

**What a write of the plugin's own asks of KeePass's windows** (measured). Read off the real types,
the code of each window `DESIGN.md` §124 lists and of every type nested in it, the entry dialog is
the only window whose code puts an entry's strings, history or attachments back, which it does at
its OK with the copy it took as it opened: content the plugin protected again while the dialog stood
goes unprotected again, reproduced with KeePass's own types. Database Maintenance removes history
items from entries and clears the database's deletion records (§639), each at the press of a button,
and puts no copy of an entry back over a write: pressed where a record counts by its history, the
removal is the loss §622 describes, which no refusal of a write would prevent, so no write is
refused for it. No other of those windows calls anything that puts an entry back or removes from it.
So a write is refused `503 busy` while KeePass's progress window is open, and while the TAN wizard
is open on the database where the write ends in a save, as every write is today; and once its plan
exists, it is refused busy while an entry dialog holds any entry the plan deletes, sets back or
protects again, the summary entry among them, since every write deletes the summary entry it finds.
Keeping an authority asks nothing more of the windows: it saves nothing, so it is written under
Database Maintenance and while the TAN wizard is open, and a group dialog holds no copy of an
entry. The store write of the settings dialog's OK is refused under Database Maintenance and under
the TAN wizard as a whole, every act of it with it; neither window can stand beside the settings
dialog, each being modal. **A pairing's and a renewal's write, a certificate given up and a
standing answer given on the renewal dialog are refused under Database Maintenance** (a later
decision): each ends in a save of the writer's own, and a press of one of that dialog's buttons
handled inside such a save was never measured. A later pull request measures it and then takes
the refusal and the watch of that dialog away, or keeps them with the measured reason. A profile
name that a pairing adds is an entry of the store (§616), and so are the admission itself and
the version of a standing answer (§618), each asked about once the plan exists, as above. *Held
by* `StoreWritesUnderDialogsTests`, `AuthorityChooserInRealDatabaseTests`,
`PairingInRealDatabaseTests.A_pairing_while_an_entry_dialog_holds_the_summary_entry_is_busy_and_spends_nothing`
and `RenewalInRealDatabaseTests`, a refusal while an entry dialog holds the summary entry and a
refusal under Database Maintenance among it.

**The write made as a node's dialog closes is asked the same** (§629; reasoning, and run in
KeePass's window, §644 item 6). The paragraph above is silent on it. That write is made in the
handler of the dialog's close, after KeePass's own handler, so the dialog that closes has put back
by then what it puts back. A group dialog holds no copy of an entry. An entry dialog whose close
writes rules is on an entry of the operator's and never on a record of the store, where the tab
takes no change (§657). So the plan of that write touches nothing that its own dialog holds, and the
write is not refused for the dialog it comes from. Every other question is asked as of any write of
the operator's (§627). Built: the write is asked what the store write of the settings dialog's OK is
asked, so it is refused whole under Database Maintenance and written by an OK made once that window
has gone. *Held by*
`StoreWritesUnderDialogsTests.The_OK_of_a_node_dialog_s_tab_is_refused_whole_under_Database_Maintenance_and_written_once_it_has_gone`
and `GrantEditorInRealDialogsTests`, and in a real KeePass by `LiveGrantEditorTests` and
`LiveRuleOkInsideASaveTests`, where no write at a dialog's close was refused.

### A write of the plugin's own §627

**When.** After an OK on the KPG settings tab; at a pairing or a renewal that the operator
approved; at an answer to a renewal that the operator gave to stand, saved as the renewal that
the operator approved is (below); when a certificate is
taken away (§648); when the operator keeps a store (§649); and when the operator raises the
store (§652). A rule set in a node's dialog is added at that dialog's OK, in memory, and the
operator's next save writes it (§629). The plugin writes at no other moment.

**The steps**, in a single turn of the writer, as measured in `model/crdt_model.py` but where a
step names another source:

1. **The store is read.** A write is refused unless a single store key stands (§649), the
   store's format number is the plugin's own and no record is of a kind the plugin does not
   know (§651, §621), and the store is whole (§624). Each of these writes is the way out of a
   single state: the press of §625 on a stopped store; keeping a store where several keys
   stand, which is held to the format of the store kept; raising a store whose format number is
   below the plugin's own (§652); Create where no key stands; and the command that starts the
   store again, wherever a key is found (§632). The measured code's Create also wrote on a
   stopped store, which §624 rules out. A write that the store has no room for is refused as
   well (below).
2. **The records of the act are added** to the plugin's group, and with them a version record
   where §650 has the write carry it (proposed). The act of a write of rules holds, beside the
   versions of the rows and of the switch that were changed, the versions at that node and
   below it that the change writes anew (a later decision, §617, §629), each replacing the
   version it renews, which step 3 deletes in the same write.
3. **What a record present replaces is deleted, with a deletion record**: every version that a
   version in force names (below); every profile addition, in force or not and whatever its
   `name`, whose `after` does not hold every removal of its name that the store or the write's
   own records know (§616), and no other addition, none that the write adds among them; every
   admission whose client key is revoked or whose issuer is retired; every authority
   that is retired; and every summary entry present, with every summary entry those name. A
   deletion record is written as well for a replaced record that this copy does not hold, so
   that the record stays out of force when a merge brings it. An admission whose certificate was
   taken away goes the same way (proposed, §648). **The admissions deleted are told by the
   texts the core keeps** (S5): every record of kind `client` present under the key whose
   `client` is revoked, whose `issuer` is retired or whose `certificate` was taken away, by a
   record, an item or a deletion record, in force or not and whether or not it reads as an
   admission (§615), the records of the write itself counting as present, so a write that
   revokes, retires or takes a certificate away deletes in that write what it refuses. An
   issuer is retired here as the file knows the authority (§623 step 2), so an admission whose
   issuer is retired only by a deletion record, where no record and no item names that
   authority, is not deleted: an admission's `issuer` makes no authority known, and nothing
   serves that authority either way (§615). No other record of that kind is deleted: a renewal
   that does not count (§648), the admissions of a certificate that several admissions name, and
   a record that does not read and names nothing refused, stay, since this step names none of
   them. The plan stays pure and reads no
   certificate. It rules out deleting by what an admission comes to: the plan would read every
   certificate, and a write would delete admissions that a merge can still let count, as a
   merge that brings a deletion record of all but a single admission of a certificate lets that
   admission admit (§615). **The versions deleted** (this document's own, from the model's
   write, which deletes whatever a version in force names). A version replaces only versions
   of its own object (§617): an identifier in its `after` that a version of another object, or
   a record of another kind, carries is passed over. A version is in force by a record of kind
   `version` in force or by an item of a summary entry in force, and so is a version of the
   write's own. A version that such a version names is deleted, every entry under its
   identifier, in force or not, with a single deletion record, only where a version that stays
   in force names it and every identifier that it names itself is named by a version that
   stays. Otherwise it stays: its record stays in force, or the summary entry goes on
   restating it where only an item carries it, and it goes on naming. So no deletion record is
   left that nothing in force names, and no answer changes for a deletion. A deletion record
   is added for an identifier so named that nothing carries and the tree does not hold, once,
   and only where its ten keyed bytes are those of the naming version's object (§609). A
   version that only a version out of force names is left. **A write whose version names a
   version of the same write is refused** (§610), so no version is written beside its own
   deletion record. This step had "every version that a present version names": a version
   present beside a deletion record is out of force and replaces nothing (§623 step 1), and a
   version that it alone names still stands (§623 step 3). **What this copy
   holds is every entry of the tree, by its identifier, a record or not** (this document's
   own): an entry with every string of the store gone stays where it is, with no deletion
   record, which a merge would take as the entry deleted, and no record is added under the
   identifier of an entry the tree holds. The plugin deletes no entry that it cannot read as a
   record of its store. *Held by* `StoreWriteTests` and `StoreWritesInRealDatabaseTests`.
4. **A summary entry is added** (§611) over what is then in force.
5. **Every record that counts only by its history is set back** (§628).
6. **The database is saved, or marked as changed**, as today for the same act (below). A write
   that is saved is believed as today.

**A deletion by the plugin changes no answer.** What step 3 deletes is already replaced,
refused or restated, and the plugin never deletes a record in order to revoke, retire, remove or
narrow. So what a copy answers before its own next write and after it is the same: a deletion
record may or may not reach another copy, and two copies that hold the same decisions do not
differ by which of them has written. That is why an admission that a decision refuses is not
counted among several admissions of a certificate (§615). **A single exception**, of a record
the plugin does not write: the `from` of an admission of a certificate taken away, which the
fourth rule of §648 asks beside the `from` of the statements, is restated by no statement where
it differs from all of theirs, and is asked until the write that deletes that admission.

**Which writes save** (this document's own, from what each act does today). A pairing, a
renewal and a certificate that its client gives up answer a client, so each is saved through
`MainForm.SaveDatabase`, read back and verified before it is answered, `DESIGN.md` §119. **So is
an answer to a renewal that the operator gave to stand** on the renewal dialog, forever or until
a moment, before the client is answered (fixed for the answers, S7, `PAIRING.md` §312): it is
written in a turn of the writer, saved, and believed as a certificate given up is, on the flag
and the file. Where the write is refused or its save does not land, it is taken back out and the
answer holds for this session instead, with the operator told at once; where the save ran over a
file that does not hold what KeePass wrote, it stays in the open database, in force and marked,
and the operator is told that the save was not confirmed. Nothing of it waits for a later save.
The settings dialog's OK and the answer to which authority to keep are the operator's, at
KeePass's own window: their records are added in memory, the database is marked as changed,
and the operator's save writes them, as the tab's changes and a kept authority are written
today, `CA.md` §204. They are in force in the gate's copy from that moment, and are lost with
every other unsaved edit where the database is closed without saving. **A lift of a standing
answer, Allow renewal, is such a record**, written with the buffer's other records at OK; a
lift lost with an unsaved database leaves the refusal of the file standing, the stricter way
(§618).

**How a pairing's and a renewal's write is believed** (S4). Beside the flag and the file of
§626, it is read back after its save by its own entries: each entry the plan added is in the
tree under its identifier, with the strings the plan gave it. A certificate given up is believed
as every change is, on the flag and the file. What a whole reading would add, a decision that
arrived inside the save, is answered at the client's next request, and a whole reading walks
every entry on KeePass's thread. It rules out believing an admission on the flag and the file
alone, and a second walk of the tree in the turn. *Held by*
`PairingInRealDatabaseTests.An_admission_missing_from_the_tree_after_its_save_is_taken_back_out_and_hands_nothing_over`;
a renewal goes through the same method of the writer. **A write whose plan adds no record of
kind `client` is refused before the condition is asked** (this document's own). Read back by
its own entries, a write that added nothing would find nothing missing, and believed on a save
of nothing, a pairing or a renewal would hand a certificate over with no admission in the file,
which nobody could see and nobody could revoke. So such a write is refused as a refused plan is:
nothing is applied or saved, the condition uses up no approval and spends no invitation, and no
certificate is handed over. A plan adds no such record in two cases. **Its act is left out
because the authority that issued the certificate is not the authority the store serves
alone**, as where that authority was recreated or deleted since the operator answered: the write
is answered as an authority that went, a pairing `409 no invitation` and a renewal `401
unpaired`, with the approval used up by the handler and the invitation unspent, `PAIRING.md`
§311 and §312. An issuer that still stands beside a second authority is no such case: that write
is answered `undecided` before anything is planned, below. The condition compares no authority,
since the plan has held the admission to its issuer before the condition is asked.
**Or the store already holds the act's own record**, which no write through the writer reaches:
the act is composed in the writer's turn and every record holds a random nonce, so that case is
held where the plan is asked alone. *Held by*
`AdmissionActsTests.A_write_that_adds_no_admission_is_not_one_a_pairing_is_believed_on` for a
plan that plans nothing;
`PairingInRealDatabaseTests.A_write_whose_plan_leaves_the_admission_out_is_not_believed_and_spends_nothing`
for an act left out, with the condition not asked; and the answers by
`PairingHandlerTests.An_authority_recreated_under_the_write_admits_nobody`,
`RenewalHandlerTests.An_authority_recreated_under_the_write_is_answered_unpaired_and_lets_the_approval_go`,
`PairingInRealDatabaseTests.A_pairing_whose_authority_is_recreated_before_its_write_is_told_its_invitation_is_gone`
and
`RenewalInRealDatabaseTests.A_renewal_whose_authority_is_recreated_before_its_write_is_answered_unpaired_with_nothing_written`.

**What an approval held for a renewal is held to** (S4). The admission it renews is compared at
the write by the identifier of that admission's entry, where today it is compared by a digest of
the record's stored text, `PAIRING.md` §312: a record is written once, so the same entry under
the same store key is the same content. It rules out a digest of a stored text, which the store
does not keep. **The approval is written only where that admission still admits its
certificate**, in the reading of the write's own turn, and not merely where an entry is present
under the identifier: an admission whose client was revoked, whose certificate was taken away,
whose issuer was retired, or that reads no more since the operator answered, makes the approval
stale, and the renewal is answered `401 unpaired` with nothing written. *Held by*
`ApprovalBookTests.A_renewal_is_written_only_while_the_admission_approved_against_is_in_force_and_reads`:
written while the admission is in force, and stale once its client is revoked, its certificate
taken away or a second admission names the certificate;
`ApprovalBookTests.A_renewal_is_stale_while_a_decision_refuses_the_admission_whose_entry_is_still_there`,
stale beside a revocation and beside a certificate taken away while the entry is still in the
tree; `ApprovalBookTests.A_renewal_cannot_be_held_without_the_entry_it_was_approved_against`;
and over a real database
`ApprovalsUnderLoadTests.A_certificate_given_up_racing_an_approved_renewal_is_never_overtaken`.

**A client's write meets the store's word** (§644 item 26). A pairing, a renewal and a
certificate given up ask the store again in the writer's turn, and a store that holds the gate
back there is answered with its word under `503`, as a connection is (§656), and nothing is
written. **An approval held for such a write is let go there** (S4), while an invitation stays
good: each word ends by an act of the operator's, after which the operator is asked again. It
rules out an approval kept for a store that serves nothing. **Built**: the writer reads the
store afresh in its turn and, where it holds the gate back, answers the word before anything is
planned, asked or spent, `split`, `outdated`, `stopped`, and `lost` for a file whose store key
went. The word is in the body and in `KPG-Unavailable`, as on a connection. **Several authorities
standing where the write is planned are answered `undecided` the same way**, after the store's
own word and in the order a connection is answered: a store that is whole and holds a second
authority, as a synchronize brings, takes no admission and no certificate given up until the
operator has kept an authority, and nothing is planned, asked or spent. **`undecided` is answered
only where the request's own authority is among the authorities standing**: the issuer of the
admission a pairing or a renewal writes, and for a certificate given up the authority its
connection arrived under. Where several stand and that authority is not among them, retired or
gone, there is no word, and the request is answered as for an authority that went: the plan
refuses an admission whose issuer does not stand alone, with no window asked and nothing
spent, saved or changed, and a certificate given up is admitted by nothing there. So that
answer, above, is for a store that is whole and does not hold the request's authority
standing, whether it serves another authority, none, or holds several: `409 no invitation` for
a pairing and `401 unpaired` for a renewal and for a certificate given up. A certificate given
up is answered `401 unpaired` as well where the store serves its authority and holds no
admission in force for the certificate. **A renewal asks the word before its write as well**,
as it arrives and again once its dialog is answered, each time in a single hold with the record
it renews, so the word and the record are of a single reading, and a store that came to hold
the gate back while the dialog stood is answered its word and not as a record that went,
`PAIRING.md` §312. **A certificate given up that the store has no room for is answered
`stopped`** (this document's own): where the admissions of the certificate hold several origins
the act needs a statement for each (§648), and a store a single item short of the ceiling has
room for its next write by §643 and none for that write. The plan refuses it for the plugin's
own ceilings, and the client is answered as for a store that has no room, with nothing written.
*Held by*
`PairingHandlerTests.A_store_holding_the_gate_back_at_the_write_is_answered_its_word_and_lets_the_approval_go`,
`RenewalHandlerTests.A_store_holding_the_gate_back_at_the_write_is_answered_its_word_and_lets_the_approval_go`
and `RevocationOperationTests.What_the_write_came_to_is_what_the_client_is_told`, each word,
`undecided` among them; and through the writer over real databases by
`PairingInRealDatabaseTests.An_admission_over_a_store_that_holds_the_gate_back_is_answered_its_word_before_anything_is_asked`,
`PairingInRealDatabaseTests.A_second_authority_arriving_while_the_dialog_stands_is_answered_undecided_with_nothing_spent`,
`PairingInRealDatabaseTests.A_summary_entry_deleted_for_good_while_the_dialog_stands_is_answered_stopped_with_nothing_spent`,
`RenewalInRealDatabaseTests.A_renewal_whose_store_stops_before_its_write_is_answered_stopped_with_nothing_written`,
`RenewalInRealDatabaseTests.A_renewal_whose_database_comes_to_hold_a_second_authority_before_its_write_is_answered_undecided`,
`RenewalInRealDatabaseTests.A_renewal_whose_store_comes_to_hold_the_gate_back_while_the_dialog_stands_is_answered_its_word`,
`GiveUpInRealDatabaseTests.A_certificate_given_up_over_a_store_that_holds_the_gate_back_is_answered_its_word_with_nothing_written`
and
`GiveUpInRealDatabaseTests.A_certificate_given_up_that_the_store_has_no_room_for_is_answered_stopped_with_nothing_written`.
An authority that is not among several standing is held by
`PairingInRealDatabaseTests.An_admission_whose_authority_was_retired_beside_several_others_is_answered_unserved_before_anything_is_asked`,
`RenewalInRealDatabaseTests.A_renewal_whose_authority_is_retired_beside_several_others_before_its_write_is_answered_unpaired`
and
`GiveUpInRealDatabaseTests.A_certificate_given_up_once_its_authority_was_retired_beside_several_others_is_answered_missing_with_nothing_written`;
the approval let go after a renewal's dialog by
`RenewalHandlerTests.A_store_that_comes_to_hold_the_gate_back_while_the_dialog_stands_lets_the_approval_go`;
and the single reading by
`RenewalInRealDatabaseTests.After_the_dialog_the_word_and_the_record_are_read_from_a_single_reading`.

**Every write goes through the writer's guard**, the settings dialog's OK among them, which
today is applied with no question asked. A write is made on KeePass's thread, after the
questions every write asks there, `DESIGN.md` §119 and §124, and after the store was read in
that same turn (step 1). A write of the operator's that is refused there adds nothing, and the
log names what was not written and why. **The settings dialog's OK meets no save of the plugin's
own** (measured, §644 item 30). A save of the plugin's own can pump under the dialog, as a
pairing's does, and a press on OK inside that pump is handled there, as the button's click. The
dialog's close, which is where the tab writes what it held, is raised by the dialog's own modal
loop and never by a pump running under it, so it comes once the save has returned, and the
write it makes is made against the store as that save left it. So the guard against a write
inside a save never refuses the tab's OK for a save of the plugin's own, and it stays for every
other write. This corrects the sentence that stood here, by which such an OK was refused there
like any other write. *Held by*
`ModalDialogCloseTests.A_modal_dialog_s_close_is_raised_only_once_a_pump_under_it_has_returned`
and, over the real writer and a real database, by
`TabOkBesideOwnSaveTests.The_tab_s_OK_pressed_inside_a_pairing_s_save_is_made_once_that_save_has_returned`.
**A node dialog's close is raised the same way**, by that dialog's own modal loop, so the write
of §629 meets no save of the plugin's own either (reasoning from that measurement, and run over
the real group dialog since, `NodeDialogOkBesideSaveTests`; the run in KeePass's window is
owed, §644 item 6). **What the operator is shown of a refused write differs
between the settings dialog and a node's dialog** (a later decision). For the settings dialog's
OK the log's line is all. Where the write at a node dialog's OK is refused, the log names it
and a message of the plugin's own is shown once that dialog has closed, with the reason
(§629).
Where the dialogs that would take a write back are not watched, which the
plugin's load logs, every such write is refused for the session, and the KPG tab offers no
command on the store and states why, as under a word (§636).

**Whether the store has room** is a property of the file, which the reading judges where the
store is whole. It works out the summary entry that a write adding nothing would write: its
`after`, which names every summary entry present, everything those name and every summary
entry that a deletion record tells of, and its items, which restate what is in force: the item
`format`; an item for each client key revoked and each authority retired, by a record, an item
or a deletion record; an item for each removal of a name and for each statement that took a
certificate away, each by its identifier (§616, §648); and an item for each standing version
and each keep record, as a record or an item of a summary entry carries it, a version that a
version in force replaces carried by neither (§611, §623). The store has **no room** where the
content that holds that `after`, beside the summary entry's height and nonce, is longer than the
plugin's ceiling for a string, or where those items leave no item name for a further item
(§643). The content is held to the ceiling, and not the `after`
alone (this document's own), since a store whose `after` fitted and whose content did not would
be served while every write into it was refused. So a store that has room takes every write a
client can ask for, each of which adds a single item at most, and no store serves while it
refuses its clients' writes. What the gate does with a store that has no room is the project's
answer to S35 (§643). A write whose own records or items do not fit what is left is refused
whole, before anything is added, also where the store has room: that is an OK on the tab that
carries more than fits, and the log names how much fits. **A version is composed only where the
item that restates it fits as well** (this document's own; §644 item 31). An item `version` is
longer than the content of the record it restates, so a version of a rule or of a switch is
composed only where its content and that item each fit the plugin's ceiling for a string
(§643). It rules out a room that holds the longest item to the ceiling at every hold, and a
version that fits while its item does not, after which every write is refused in a store that
is served. Measured for the probe's strings (`measured-rules-owed-forms` 7): the edit that
takes a row's item past the ceiling comes while the content of the summary entry still fits.
**A version that no longer fits is left without its renewal, and the rest of the write is
written** (a later decision). A version's `after` grows by 33 characters with every edit of
its row and with every renewal by a write above (§617), and passes the ceiling at about 7,940
identifiers, a figure computed from the strings of the measured code over 400 edits and 400
renewals on files, and reached in no file (`measured-rules-owed-forms` 7,
`measured-rules-across-levels` 9; §643). At that ceiling the write above leaves that single
version as it is, the log names its node, and every other version of the write is written: the
OK is not refused as a whole. The version left goes on cutting to what it recorded, which is
narrower under a widening, and under a narrowing the node gives what arrives. Refused whole, a
narrowing above would not be written at all for as long as a version below stood at the
ceiling. The same holds of a version of the act's own that does not fit (this document's own):
it is not composed, its row stays as it stood, the log names it, and the operator is shown the
message of §629 for the rows that were not written. *Held by* `RuleActsTests` and
`StoreWriteTests`. From half of either
ceiling the reading states how far the store has grown, for the log where a reading first finds
a store it serves there and at every write, and for the tab (§643). *Held by* `StoreRoomTests`:
a store at each ceiling has no room, the same store a summary entry or an item short of it has
room, and two stores that each have room, synchronized, are judged by what the next write would
name.

**A write that does not land is taken back out**, as today: the entries it added are removed
with no deletion record (the design paper 10), and the entries it deleted are put back with
their deletion records removed (reasoning). A record that it set back is put back as it was
edited, with the history it had; a group that it made is removed; and a deletion record that
it added for a record this copy does not hold is removed. *Held by*
`StoreWritesInRealDatabaseTests.A_part_of_a_write_taken_back_by_its_undo_leaves_the_database_as_it_was`,
each part of a write's application alone and every part in the order a write takes them; and,
after a save that fails, by the fixtures of the writes that save, below: the database holds the
entries, the histories, the group and the deletion records that it held before the write, and
reads as it read before. **A certificate given up that is taken back out leaves the database's mark as it
was before the write** (this document's own). The application of a write marks the database as
changed; for a certificate given up the writer puts the mark back at once and decides it as for
every change, from what the database held before and from the file, so a write taken back out
over a file that did not change leaves nothing to save. *Held by* `GiveUpInRealDatabaseTests`:
`A_certificate_given_up_that_no_save_kept_is_taken_back_out_and_stays_admitted` and
`A_save_that_fails_before_writing_the_file_takes_the_certificate_given_up_back_out`, each with
the store as it was and the database unmarked; and by `PairingInRealDatabaseTests` for an
admission, whose save failures leave every entry and every deletion record of the store as
before, an admission that had come back beside its revocation among them
(`A_pairing_that_does_not_land_puts_an_admission_that_came_back_back_as_well`).

**The settings tab still buffers.** `PendingDatabase` holds records to add where it holds keys
today, OK applies them in a single turn, and Cancel drops them. The merges the tab registers
today become properties of the records (the design paper 10, not run): a removal of a name
defeats an addition that a pairing wrote underneath the dialog, since that addition did not see
it; **Allow renewal** is an answer whose `after` is what the page showed when it was pressed, so
an answer the renewal dialog wrote underneath is made apart and the refusal stands; and a
revocation is an addition that cannot fail in the buffer.

**What the buffer holds for a name** (this document's own). An act about a name, an addition or a
removal, holds what is to be done, as an act on a client does: it is composed again wherever the
buffer is read and at OK, under the key that stands then. **The buffer holds the operator's last
press for each name and no earlier press**: a press about a name takes every act held about that
name out of the buffer, and is held itself only where it still has something to write against
the store with the acts left. So a name added and removed on a single dialog, or removed and
added again, writes nothing; written as records, the pair would leave an item in every later
summary entry for good. **An addition names the removals of its name that the page's reading held
at the press**, those a deletion record tells of once the name counts as known among them
(§616). Where the store at OK knows a removal of the name beyond those, counted the same way, or
has the name on the list, nothing is written for the press, and the log names it. A removal is
written where the name is on the list at OK. That is the reading this section gives **Allow
renewal**: a removal made apart, which the operator never saw, is not overruled by a press made
before it arrived, as §610 has records made apart give the narrower answer. It rules out an
addition composed at OK against whatever stands then, and an act composed once under a key that
went.

| Under the dialog | Held on the tab | At OK |
| --- | --- | --- |
| a pairing adds the name `x` | nothing about `x` | `x` stays: nothing of the tab's is written over anything |
| a pairing adds `x` | Add `x` | nothing is written for the press; `x` is on the list by the pairing's addition |
| a pairing adds `x`, and the page then shows it | Remove `x`, pressed on that row | a removal is written, and the same write deletes the pairing's addition, which did not see it |
| a merge brings a removal of `x` that no addition names | Add `x`, pressed before | nothing is written, and the log names the press |
| nothing | Add `x`, then Remove `x` | nothing is written |
| nothing | Remove `x`, then Add `x` | nothing is written, and `x` stays as it was |

**The Profiles page's line names a removal made apart** (this document's own) as why a name the
last press added does not show, and only where that is why: `PendingDatabase.OvertakenNow` finds
the additions held that a removal the press never saw leaves out, as the OK's write finds them,
by reading alone. For any other cause, the authority the name was added under gone or the store
split or stopped under the dialog, the line states only that the store as it stands cannot take
the name. Either way OK writes nothing for the press and the log names it; the name comes back by
being added on the page again. *Held by* `ProfileListTests` and `PendingRecordsTests`.

**What the buffer holds, and what OK does with it** (this document's own). Each press on the
tab puts its act into the buffer. An act of the authority's, a retirement or an authority with
its certificates and its keys, is composed at the press, from the store as the page showed it.
**An act on a client, on a certificate or about a name holds what is to be done**, the client to
revoke, the certificate to take away or the name to add or remove, and its records are composed
wherever the buffer is read and in
the write at OK, against the store as it stands then with the acts pressed before it planned
over it together, as the single write OK makes of them. So a revocation is written under the
store key that OK finds, a certificate taken away names the renewals the store holds at OK, a
renewal that landed under the dialog among them (§648), and the second of two presses sees the
first. **The pages read the store with every act held planned as that single write**, applied
in memory over the live entries, so a page shows what OK would write now: a single summary
entry more than the live store however many acts are held, and so the room the page shows,
the state and from half of a ceiling the count of writes that still fit (§643), is the room OK
leaves. Where that single write would be refused or would leave nothing, the pages read the
store as it is. **The buffer composes each act once**, over the live store with the acts
composed before it planned together, and keeps what it composed until the store or the acts
held change: a press composes its own act alone, and a read of the buffer that finds neither
changed composes and plans nothing again. An authority made on the dialog and retired on the
same dialog leaves the buffer with its retirement and reaches no database, as today, and where
that retirement held the store key the retirements left in the write hold it, so a Recreate
and then a Delete on one dialog write what the Delete alone writes (this document's own). At OK
the acts are held, in the order they were pressed, to the store as it stands then. An act
whose ground went meanwhile, as a Recreate of an authority that no longer stands, is left out,
and the log names it, and so is an act composed against a store key other than the key that
stands then, or composed where no key stood while a key stands then: a Create composed under a
key that went since never starts the store again under it (§632). A press is planned with
every act held, against the store as it stands, as OK plans them, and is refused with nothing
held where OK would refuse them all or leave it out. Every other act's records are written in
a single write, with the summary entry over what is then in force. A revocation has no such
ground and is always written. A certificate taken away has none either, and is written unless
nothing of the certificate is left to end by then: a statement took it away, by its client or
in a copy merged in, or no record of kind `client` in force names it under a client the store
still admits, as where a revocation ended it with its client or its records were deleted by
hand, a deletion merged in under the dialog among them (§648). An act that comes to nothing so
is refused at the press with nothing held, with "the act no longer applies to the store as it
stands", and at OK it writes nothing and the log names the press and that nothing was left to
write. **What follows the write runs in a fixed order.** Once the outcome of the store's write
is known, the log tells what it came to, each act that landed or what was not written and why;
then the audit's lines are written (§648); and the buffer is let go last, the store key each
act holds overwritten, whatever the write and the lines threw. Cancel drops the
buffer, and a store key that was made on the dialog goes with it. **What an act refuses reads
as gone while the act waits** (this document's own): the pages read the store with the acts
planned over it in memory, and the plan of that write deletes what the acts refuse, so the
rows of a client revoked on the page, and the admissions of an authority that is being
recreated or deleted, are gone from the pages before OK and are deleted in the database at OK
only where the write took the act (§614, §615). *Held by* `PendingRecordsTests`: a revocation
pressed beside a Recreate
whose authority was retired underneath the dialog is written, and the Recreate is not; an act
left out at OK leaves a revocation refusing an admission that a copy brings, and an act written
at OK deletes every admission of the authority it retires as the store stands then; a Recreate
and then a Delete write what the Delete alone writes; a press that OK would refuse with the
acts held is refused with nothing held; a Create and a Delete on the same dialog write nothing
and change nothing; a page reads a revoked client as gone before OK; a revocation is composed
under the key the store holds at OK
(`A_revocation_held_for_OK_is_composed_under_the_key_the_store_holds_at_OK`); a revocation
held while the store lost its key stays as pressed and writes nothing
(`A_revocation_held_while_the_store_lost_its_key_stays_as_pressed_and_writes_nothing`); each
act is composed once until the store or the acts held change, with the live store read no
more often as the acts held grow
(`A_sitting_of_presses_composes_each_act_once_until_the_store_or_the_acts_held_change`); a
store near the ceiling of its room reads whole through the buffer, with a single summary
entry more and the count of writes that OK leaves
(`A_store_near_the_ceiling_of_its_room_reads_through_the_buffer_as_the_single_write_OK_makes_of_every_act_held`);
and an OK whose lines throw after the write landed has its line of what landed in the log,
the database marked and its buffer let go last
(`An_OK_whose_lines_throw_after_the_write_landed_lets_the_buffer_go_last`). And by
`StoreWriteTests`: an act composed against another key, or where none stood, is left out; a
retirement dropped with the authority it retires leaves its hold on the key to the retirements
left; and an entry the tree holds gets no deletion record and no second entry. And over a real
database by
`TabReplayOverRealDatabaseTests.A_certificate_given_up_under_its_client_s_waiting_X_and_the_revocation_both_stand_at_OK`:
a certificate that its client gives up while that client's revocation waits on the tab stays
given up, and the revocation is written at OK beside it.

**What is built.** The writes of the authority's commands (§614): Create, Recreate and Delete at the
settings dialog's OK, buffered by `PendingRecords`, and keeping an authority; and the seal of
§625, the only write taken over a stopped store, its act first and every act after it planned
over the sealed store, which deletes every entry that is damage with a deletion record and names
each damaged summary entry it deletes in its summary entry's `after`. Each is planned by
`StoreWrite` against the store
read in the writer's turn, steps 1 to 5 with the room held at the plan as at the reading, so that a
write the room allows is never refused for a ceiling, and applied to the database's entries by
`StoreEntries` on KeePass's thread through the writer's guard, which marks the database as changed
and saves nothing. Once the plan exists, every entry it deletes, sets back or protects again is
asked about, and the write is refused while an entry dialog holds any of them (§626). The store's
own write of a Recreate, from its reading in the writer's turn through the plan and the entries
asked about to the entries changed, and without the writer's own questions or the redraw of
KeePass's window, took 13 ms at the median beside ten thousand entries and a store that starts at
282 entries and gains an entry with each write, and 32 ms where every entry holds ten history items;
the first write of a run is slower, as warm-up (`StoreReadingCostTests`, `DESIGN.md` §119). A write
the application cannot finish is taken back out at once. **The writes that save are built**: the
admission of a pairing and of a renewal (§615), a certificate that its client gives up (§648),
and a standing answer to a renewal given on the renewal dialog (§618).
Each is a single turn of the writer: the store is read afresh with every entry
(`KdbxStore.ForWrite`) and written into only where it holds nothing back; the act is planned and
every entry the plan touches asked about (`KdbxStore.Stage`); the caller's condition, which uses
up an approval and spends an invitation, is asked last, against the authority served and the
admissions of that same reading; the plan is applied and its undo kept (`StoreEntries.Apply`);
then the save, and the write believed or taken back as this section has it. An admission is read
back by its own entries, and a certificate given up is believed on the flag and the file. Taken
back are the entries added, the entries deleted with their deletion records, the deletion records
added, the records set back and a group made. A database
that closed inside the save is taken nothing back from, and no certificate is handed over. A write
that stands has KeePass's group tree and entry list rebuilt, since it adds and deletes entries of
the plugin's group, `DESIGN.md` §119. **What a pairing's write holds KeePass's thread for**
(measured by `StoreReadingCostTests.What_a_pairing_s_write_holds_the_thread_for`, explicit, in
Release, the median of 21 pairings after a first, each a new client with a profile, beside
10,000 entries in a hundred groups and an authority the product created, each series on a
database of its own): the writer's whole turn with a save that writes nothing, which is the
reading with every entry, the plan, the entries asked about, the application, the record read
back and the file hashed back, took 7.1 ms, and 34 ms where every entry holds ten history
items. With KeePass's own save of the database the turn took 125 ms, and 1.13 seconds, 1.19 at
worst, where every entry holds ten history items. So the save is what grows, and the store's
part of a pairing's turn is mostly what its reading costs (§620), beside two reads of the file
to hash it: the write finds every entry it touches in the entries its reading's walk passed,
where a walk of the tree for each step that finds entries took 55 ms and 77 ms in the same
series. **A pairing that approves 32 names the list does not hold**, each an addition of the
admission's own write, took 18.6 ms at the median and 31 at worst against 7.1 ms in the same
series for a pairing whose name the list held, and 49 against 34 ms where every entry holds
ten history items
(`StoreReadingCostTests.What_a_pairing_s_write_approving_32_new_names_holds_the_thread_for`):
about 11 to 15 ms for the most names a pairing can ask for. Each figure was measured with no
settings, group or entry dialog open. The KPG tab of each such dialog open as the record lands
reads its own database's store again inside the same turn, the settings tab for its redraw and
the grant editor for its pick list (`GrantPanel.Resync`), since the notice that a record landed
names no database and each tab shows what its own database holds: each adds a reading of a store
(§620) to the turn. **The operator's revocation is an
act**, held by the buffer beside the authority's acts and written at OK. Every write deletes
the records of kind `client` that step 3 names, by the texts §658 keeps, the write's own
records counting as present, so a revocation, a certificate taken away, a Recreate and a Delete
each delete in their own write the admissions they refuse. **Every write deletes the profile
additions that step 3 names** as well: each record of kind `profile` present under the key, in
force or not and whatever its `name`, whose `after` does not hold every removal of its name that
the store or the write's own records know, a removal known as a record by its identifier, as an
item by its `id` and as a deletion record by its own identifier; no other addition, and none the
write adds. So what this deletes is an addition a merge brought, or an addition that a removal
arrived after. **Every write deletes the versions that step 3 names, where no answer changes for
it.** A version names only versions of its own object (§617): a name in its `after` is of its
object where a version present or a version the write adds carries the name with that object,
and a name that a version of another object or a record of another kind carries is passed over,
so the reading holds a version named only by a version of another object standing. A name
nothing carries may be of any object: it is held named as a name of the object is, and a
deletion record is written for it only where its tag, its subject hash and its number are those
of the naming version's object under the key (§609), which cannot tell apart the rows of two
names whose tags meet, nor a rule's object with no `/` from a row at its node whose name's tag is
0. A version is in force where a record of kind `version` in force or an item of a summary entry
in force carries it, and it names what its record holds, or its item where no record carries it.
A version in force that a version in force or a version of the write names stays in force after
the write only where a name of its own is named by no version that stays; every version in force
that nothing names stays, and so does every version of the write. What stays is settled at once,
round by round, so the outcome does not depend on the order the entries are walked in, and copies
that hold the same entries settle alike. A version that does not stay has every entry under its
identifier deleted, with a single deletion record, and is restated no more; a version that stays
keeps its record in force, or is restated by the summary entry where no record in force carries
it, and goes on naming. So no deletion record is left that nothing in force names, where the
`after` of a version is not closed as well, and no record is left in force that nothing restates.
A version present out of force names nothing, and its record is deleted where a version in force
or of the write names it; a version that only a version out of force names stands (§623 step 3)
and is left. A deletion record is written, once, for each name of a version that stays that
nothing carries and the tree does not hold. A write whose version names a version of the same
write is refused (§610), so no version is written beside its own deletion record, and a write
whose records the file holds already writes and deletes nothing. The summary entry restates each
standing version and each version that stays that no record in force carries, and no other
version. **What keeps a version
replaced out of force where a merge brings it back** is the version that names it, and its
deletion record only where its entry is older than the deletion: KeePass applies a deletion
record only to an entry last changed before it, drops the record otherwise, and only Synchronize
carries a deletion record. In the merges run, under each method in each direction and whatever
became of the entries' times, the version replaced came back with its deletion record dropped
wherever its entry was last changed in the second of its deletion or after it, and did not
stand; where its entry was last changed before the deletion, Synchronize deleted it in the copy
that held it and did not bring it back into the copy that replaced it. **The entries a write
deletes, sets back and protects again, and the entries it asks about, are found by their
identifiers in the entries the walk of the write's own reading passed**, a single map for the
write, and each group that loses entries is written once: the store's write deleting 2,000
replaced versions beside 10,000 entries took 76 ms at the median of 21 after a first, against
106 ms for a write that walked the tree for each of those steps, and 85 against 121 ms where
every entry holds ten history items, each the median of three series run in turn
(`StoreReadingCostTests.What_a_write_deleting_2000_replaced_versions_holds_the_thread_for`,
explicit, in Release). **The acts about a name are built as the buffer holds them**: the last
press for each name, composed again at OK, the additions of a pairing in the admission's own act,
and an addition left out at OK for a removal the press never saw named in the log.
**A client's write is answered the
store's word** where the store holds the gate back in the writer's turn, above (§644 item 26).
**The tab's OK meets no save of the plugin's own**, above (§644 item 30), so nothing was built
for it. **The single removal on the clients page is written as the revocation is**, by the
dialog's OK through the writer's guard (§648). *Held by* `StoreRecordTests`, against the
records of the measured code (`tests/Shared/StoreRecordVectors.json`); `StoreWriteShapeTests`,
against its writes (`StoreWriteVectors.json`): each sequence played through `AuthorityService`
and `AdmissionActs`, and each step's deletions, deletion records, records present, summary
entries, store keys and answer compared by kind and by count, the kind `client` among them,
with
`Two_copies_one_revoking_a_client_the_other_renews_synchronized_admit_nobody_of_that_client`
holding that both copies end with the same records and the renewed certificate refused in each;
`StoreWriteTests`, each step of the plan alone, the ceiling at its boundary and a record written
once among them, and a record of kind `client` deleted where its client is revoked, its issuer
retired or its certificate taken away, by a record, an item, a deletion record and the write's
own record, in force or not and read or not, and left where it names nothing refused
(`An_admission_that_reads_is_deleted_by_what_refuses_it_and_one_nothing_refuses_is_left` for an
admission that reads), and a profile addition deleted where a removal of its name that it does
not name is known by a record, an item, a deletion record and the write's own record, in force
or not and whatever its name, and left where it names every removal of its name or is the
write's own, and a record of kind `version` deleted where a version in force names it, by a
record, an item of a summary entry in force and the write's own record, in force or not, left
where only a version out of force names it, every standing version left and restated and no
version replaced, an identifier named that the tree does not hold told of by a single deletion
record where it is an identifier a version of the naming object has and passed over otherwise, a
version of another object never deleted, a version left where what it names would be named by
no version staying, a write whose version names a version of the same write refused, and the
deletion records of a write that starts a store;
`PendingRecordsTests`; `StoreWritesInRealDatabaseTests`, the group a write goes into, a write
applied to a database read as the same write applied in memory, and a version replaced deleted
and put back by the write's undo with its deletion record removed among them;
`MergeInRealDatabaseTests.A_replaced_version_a_merge_brings_back_stays_out_of_force_under_each_method_in_each_direction`
and
`MergeInRealDatabaseTests.The_deletion_record_of_a_replaced_version_takes_an_older_copy_of_it_out_by_Synchronize`;
and the writes that save by `PairingInRealDatabaseTests` and `RenewalInRealDatabaseTests`, among
them
`A_pairing_deletes_an_admission_that_came_back_beside_its_revocation`,
`A_pairing_leaves_the_admission_of_an_expired_certificate_where_it_is`,
`A_pairing_that_lands_rebuilds_the_tree_and_the_entry_list`,
`A_pairing_for_a_name_that_was_removed_names_the_removal_and_deletes_the_addition_it_defeated`
and `A_pairing_whose_save_fails_puts_back_the_addition_its_write_deleted`, and by
`GiveUpInRealDatabaseTests` for a certificate given up.

### A record written again §628

**An edited record is written again at the plugin's next own write** (decision 18). Whenever the
plugin writes to the store anyway, it also sets back every record that counts only by its
history.

**The form**, as measured in `probe/Store.cs`, `rewrite` with its form `backup`: in the same
entry, under the same identifier and in the same place, the edited version is first put into the
entry's history as an edit through KeePass's own path puts it, which trims the history to the
database's limits; then every `KPG.` string of the history item that passes becomes the current
version's again, with its protection, and every other `KPG.` string of the current version goes;
then the entry's modification time moves. Nothing else of the entry changes.

**The edited version stays in the entry's history** (decision 24), so what a person did to the
entry stays visible there. Emptying the history was run as well, and no measured outcome differs
(`measured-two-readings` 2.1, 2.6).

**Never after a merge, never on a read, never from a copy.** After a merge it would be the repair
that §630 rules out. A record whose current version passes is not written again, and the summary
entry never is, since every write replaces it.

**A string whose protection was taken off is protected again in the same pass** (S18). The
check covers a string's text and not its protection (§608), so the content of an authority, or
of a retirement that holds the store key, can pass unprotected, and KeePass's entry view then
shows the store key and the authority's private key in clear. Whenever the plugin writes to the
store anyway, it sets such an entry back as it sets an edited record back: the version with
the protection off goes into the entry's history, and the current version holds the same text,
protected. It is done at no other moment, for the reasons above. It rules out leaving the
string as it is, under which a single change in the entry dialog shows both keys for good, and
it rules out a write made for this alone, which would be a write at a read. Not run. *Held by*
`StoreWritesInRealDatabaseTests`: an authority's content with its protection taken off is
protected again after the plugin's next write, passes as before, and holds the unprotected
version in its history.

**What is built.** Both: every record that counts only by its history is set back, and every
record holding the store key has its content protected again, at every write of the plugin's
own and at no other moment. *Held by* `StoreWriteTests` and `StoreWritesInRealDatabaseTests`,
under a database that keeps ten versions and under a database that keeps a single version, and in a
real KeePass by `LiveStoreSetBackTests`.

**What was measured** (`measured-two-readings` 2), on real files on top of the store as decided:

- **With a single gate**, none of 500 reads ended wider: 337 as decided, 20 narrower, 124 with the
  whole database refusing and the store key held, 18 refusing with no key held, and 1 with
  nothing served. Every read that is not as decided follows an operation after which no version
  passes or the entry is gone.
- **Edits and writes alternating**: an entry written again after every 10 or fewer edits never
  reached the stop, over 30 edits in the longest sequence.
- **With two gates**, each of 444 merge cells ended as the same cell with nothing written again.
  The same edited record written again in both copies, and a copy writing it again while the
  other copy holds a later edit, read as decided in each of 324 cells. **No record written again
  undid a later write of the other gate.**

**What it moves, and what it does not.** The stop moves from the 11th edit since the record was
written to the 11th edit since the plugin last wrote. The 11th edit between two writes still
takes the written version out, and a database that keeps no history still loses it at the first
edit of the content. Where no version passes, the store has stopped and the gate cannot write,
so nothing is written again.

**What it costs is the entry's history.** Each edit followed by a write adds two items, and at
KeePass's limit of 10 the history of an edited record holds the plugin's rewrites and the edits
between them and nothing older.

### A rule set in a dialog §629

**A rule set in a group's or an entry's dialog waits for the next save** (decision 11), **in the
database, in memory** (a later decision). The plugin adds it at the dialog's OK, as any edit
made in that dialog is in the database from its OK, and nothing is hooked into saving. It is in
force from the OK, the plugin starts no save for it, and the operator's next save of the
database writes it with everything else that is unsaved. A rule that is not changed adds
nothing, and Cancel discards the change (the design paper 10).

**What is added at the OK** is a version for each row that was changed, a version of the switch
where it was changed, and a summary entry. The model ran it with a version of the node's whole
rule. In the run with a record for each node and profile, a row was changed where the act
changed its standing value, and a row that the act left as it read wrote nothing
(`measured-rules-per-profile` 1). **And the versions that the change writes anew** (a later
decision, §617): at the node, the versions that take a rule off and the versions of the switch
that §617 has a rule taken off, the switch turned to let the walk go on, or the press that
settles write anew; and below the node, by the write above, the versions that were letting
through everything that arrived and that the tab's reading held. The versions at the node are
composed first, the rows and the switch as the OK leaves them with the versions written anew
there, and then the write above, the nearer node first, each node judged against the reading
of the write's turn without the act and with what the act has composed so far. As run,
a row of the root changed above k nodes that let its profile through wrote k + 1 versions and
k + 2 deletion records, and left no entry and no item more than before
(`measured-rules-across-levels` 9). What such an OK holds KeePass's thread for is in §643. In
the dialog that adds an entry, the letters that arrive are those arriving at
the group the entry is being added to (this document's own; in no run).

**It is a write of the plugin's own, whole** (S15). §628 has every write of the plugin's own set
edited records back, and S15 left open whether this write does. It does. The versions are the
records of its act, and the write takes every step of §627 but the save: the store is read
afresh in the writer's turn and is written into only where step 1 of §627 lets a write in, what
the records then in force replace is deleted, a summary entry is added, every record that
counts only by its history is set back, a key's content is protected again (§628), and the
database is marked as changed. A write that added entries with no plan would put a
summary entry over a store that nothing had judged. **It goes through the writer's guard**
(§627), as the settings dialog's OK does. It is made in the handler of the dialog's close,
where the dialog's result is OK, after KeePass's own handler has applied the dialog's fields:
nothing is written at a press, and any close of the dialog but OK discards the change. That
handler is a handler of KeePass's window events, a kind of site that `DESIGN.md` §141 lists.
The act is composed and planned against a single reading, made afresh in that turn. *Held by*
`GrantEditorInRealDialogsTests` and `StoreWritesUnderDialogsTests`.

**A write that is refused there adds nothing, and the operator is told** (a later decision; §627
and this section were silent on it). The log names the node by the first characters of its
identifier, what was not written and why (§617), and once the dialog has closed a message of
the plugin's own states that the rules changed on the KPG tab were not kept, with the reason.
This had the message as KeePass's own: a box of KeePass's own counts among the windows KeePass
is waiting on, under which every write of the API's is refused for as long as the box stands
(`DESIGN.md` §119), so the plugin shows its own, posted from the handler of the close so that
KeePass has handled the dialog's result first. A
change of rules that was not written can be a narrowing that the operator takes to be in force,
so the log alone is not enough for it. The tab is read only wherever the refusal can be known as
the dialog opens (§657), so the message is for a refusal that arises while the dialog stands,
as where a synchronize started by a trigger stops the store, or where the change does not fit
the store's room (§627). KeePass has closed the dialog by then and keeps its own changes to the
group or the entry. The settings dialog's OK keeps the log alone (§627). The message's text was
proposed as "KeePassGate did not keep the rules changed on the KPG tab:" and the reason, and
"What is built" below has the texts as built, which name no profile.
**Considered and not taken:** the log alone, as for the settings dialog, under which a rule
that the operator narrowed stays as wide as it was with nothing on the screen.

**What the editor's buffer holds** (this document's own, in the manner §627 has for a name). For
each row the operator touched, the buffer holds what the row is to be, and for a rule in several
versions each version an X removed (§657), and with it the versions the tab showed for that row at
the press. For the switch it holds the same, and only where the operator pressed the check box, also
where the switch stands in versions that differ (§657). **With each press that takes a rule off,
turns the switch or settles a limit, it holds what the tab showed arriving at the press**, the
letters for each profile the press concerns, which the write cuts to what arrives at the node in its
own reading before it records them (a later decision, §617), **and the identifiers of the versions
that let through below the node** in the tab's reading, so that the write above renews no version
that the operator's tab did not hold (§617). A later choice of a rule for a row takes the place of
an earlier choice, and of the removals before it: where versions of a rule were removed until a
single row was left and that row was then given a rule on the same tab, OK writes a single version
that names every version the tab showed. At OK a row is written only where the store, as it reads in
the write's turn, does not give what the row is to be from a single standing version, so a row the
operator did not touch, or set back to what it was, writes nothing. The new version names in `after`
the versions the tab showed at the press and everything those name (§617). A version that reached
the database under the dialog, as a synchronize started by a trigger brings it, is not named: it
stands beside the new version, and the gate grants what both allow until the operator has removed a
version (§657). That is §610: records made apart give the narrower answer, and a change the operator
never saw is not overruled by a press made before it arrived. While the dialog stands, the tab reads
the store again where a record lands: a row the operator did not touch follows the store, and a row
the operator touched keeps its press and the versions it named. It rules out a version composed at
OK against whatever stands then. *Held by* `GrantSheetTests`, `RuleActsTests` and
`GrantEditorInRealDialogsTests`.

**What the write leaves of the node** (reasoning, and as run in KeePass's window, the mark and
what KeePass asks before it ends with a rule unsaved among it, §644 item 6). A rule is no part
of its node. A change of rules alone gives the
node no new modification time and an entry no history item, and KeePass's own question whether
to keep an entry's changes is not asked for it: an entry's dialog closed by Escape after a
change of rules alone asks nothing, and the change is discarded. The plugin's write marks the
database as changed, so KeePass asks before it ends with a rule unsaved.

**What it costs.** A rule is lost if KeePass ends, or the database is closed, before a save, as
any unsaved edit is (§642).

**Considered and not taken: holding the rule in the plugin's memory and adding it at the event
that precedes a save.** A plugin's addition at that event reached the file in every run that
tried it. The event is also raised inside the save that ends a synchronize, with the merged
database in memory (`measured-desktop-windows` 6.2), so a version made there is made after a
merge, beside versions of another gate that the operator never saw.

**What was measured.** A rule waiting in memory was run in the model
(`measured-store-as-a-whole` 1), and in KeePass's window to the file, with the product's own
write at the dialog's close (§644 item 6).

**The editor no longer writes into the dialog's copy of custom data.** The grant editor keeps its
change in a buffer of its own and applies it when the node's dialog closes with OK (the design
paper 10). What the editor shows for a rule that stands in several versions is §657.

**What is built.** The write at a node dialog's close, with everything it writes at the node
and the write above. `GrantSheet` is the buffer: it keeps, for each profile, the last press
with the node's rules as the tab showed them then, what the tab showed arriving and the
standing versions below the node as the tab's reading held them, and for the check box the
same; a row or a box brought back to what the store gives from a single
standing version, or for a cleared box from none, holds no change. `GrantPanelFactory`
subscribes to the dialog's close as the tab is built and, where the dialog's result is OK and
the sheet holds a change, hands the changes to the store through `DatabaseWriter.ByOperator`.
There `KdbxStore` reads the store once with every entry, `RuleActs` composes the act over that
reading, with the node's parent of the same reading for what arrives and the tree of the same
turn for the nodes below, and the write is planned and applied against that very reading, the
database marked as changed and nothing saved.
Cancel, Escape and the close box write nothing, and KeePass asks nothing about a change of
rules alone. In the dialog that adds an entry, the parent is the group the entry is added to.
**A change that was not kept is told**, in the log and in a message box of the plugin's own,
posted to KeePass's main window from the handler so that KeePass has handled the dialog's
result first, and shown at the first moment KeePass is not part way through work of its own,
since a box shown inside a save of KeePass's would stand inside that save. A write refused
whole is told as "No change made on the KPG tab was kept:" and the writer's reason. Where the
write was taken, every press of the operator's own that was not written is told by its reason,
a line for each reason and no line twice: the new version of a rule no longer fits the room the
store gives (§627); the rules of the group or entry changed in the database while the dialog
was open, which is a limit settled where the write reads the switch refusing, or versions
removed that leave none to write again; a change that is in no form the store takes; and a
store that holds no key or cannot be read as a whole. The message begins "Not every change
made on the KPG tab was kept." and ends with a line telling that the KPG tab of the group or
entry shows the rules as kept, or begins "No change made on the KPG tab was kept." where
nothing was written. A press made before a reading made again found the database no longer
taking rules is told as not kept, with that reason, and a defect that stopped the write is
told as a write that may not have been kept. **What no press of the operator's is, the log
alone names**: a version left that another press wrote anew, a version below the node that the
write above left at the ceiling, and a node below that the write above did not carry along
because the tab had not held every version standing there at the press, in a line of its own
kind (§617); and everything where KeePass names no main window.
The log names the node a change or a version is at by the first eight characters of its
identifier, a node below for a version the write above left, with the profile of the row or
"the box". A
defect in the handler is counted as an incident, `DESIGN.md` §141. The versions below the node
that a write above writes anew are written in the same write, with the identifiers of them that
the sheet keeps at each press (§617), and what an OK above many nodes holds KeePass's thread for
is in §643. **In a real KeePass** (§644 item 6) the OK of the group dialog, of the entry dialog
and of the Add Entry dialog is in force for a client before any save and marks the database,
the entry dialog's with nothing of the entry changed, and the operator's save writes it, while
Cancel and Escape write nothing. KeePass asks before it closes, locks and exits with a rule
unsaved, saves the rule with an entry under its automatic saving after an entry's dialog, and a
save over a file another copy replaced does to the rule what it does to every change of the
window: Synchronize keeps both copies' rules, Overwrite keeps the window's and loses the other
copy's, and Cancel writes nothing and leaves the database marked. An OK pressed inside a save
of the plugin's own is written once that save has returned. The X on a version, the X on a
limit and an OK above a node that follows reach the file and the client as §617 has them, and
the message after an OK that left a press out comes once the dialog has closed. *Held by*
`GrantSheetTests`, `RuleActsTests`, `WindowHandlersTests`; and with the real `GroupForm` and
`PwEntryForm` by `GrantEditorInRealDialogsTests`, OK writing the rule with the entry unchanged
and the database marked, Cancel writing nothing, and a refused OK adding nothing and told once
the dialog has closed; `NodeDialogOkBesideSaveTests`, an OK pressed inside a pairing's save
written once that save has returned; `StoreWritesUnderDialogsTests`, the OK refused whole under
Database Maintenance; and in a real KeePass by `LiveGrantEditorTests`,
`LiveRuleWaitsForTheSaveTests`, `LiveRuleOkInsideASaveTests` and `LiveRuleSeededStatesTests`.
`SnapshotHazardTests` keeps the group dialog's copy of a group's custom data measured, as the
reason nothing of the plugin's is kept on a node.

### What the plugin never does §630

- **It never changes the content of a record it wrote.** The single change it makes to an entry
  of the store is setting an edited entry back to the version it wrote.
- **It never deletes a record to take something away.**
- **It never reads a copy of a record and never writes a record from a copy.**
- **It never puts anything on the operator's entries or groups, and never writes into custom
  data.**
- **It never decides by a time or by the place of a record.**
- **It keeps nothing about a database outside the file.**
- **It writes nothing at an open or at a read.**
- **It repairs nothing after a synchronize** (decision 14). The store's own reading decides what
  a merge leaves.

**Why no repair.** A repair was measured: after a Synchronize at a gate, the plugin wrote again
whatever took access away before the merge and no longer did after it. With several gates it
cannot tell a merge that undid a refusal from a later decision made at another gate. It closed a
widening in 1 of 480,000 histories of the search and in a single sequence on real files, and it
made a file narrower than decided, writing a rule widened, a name added again or renewals
allowed again at another gate back to the earlier state, in 8 to 74 histories of 20,000 a cell
(`measured-store-as-a-whole` 2.6, 3.7; `measured-decided-store` 7). With KeePass's trigger system
off no synchronize is announced to a plugin, and an import raises no event at all
(`measured-desktop-windows` 6.3), so a repair would also not have run where it was needed most.
Run without it, no cell on real files ended wider that was not wider with it
(`measured-decided-store` 2.1).

### Several gates §631

**Several gates for a database are supported** (decision 13). Each gate answers from its own
copy, and every gate writes.

- **A gate that has not yet taken the file in still admits a client revoked at another gate**,
  until its KeePass takes the file in (§639).
- **Several gates' versions of the same profile's rule at a node give what all of them allow**
  until the operator has removed versions. Changes to different profiles at a node do not meet
  (§617).
- **A name removed at a gate stays removed** against an addition that had not seen the removal
  (§616).
- **Two gates that each create an authority leave two authorities standing**, and nothing is
  served until the operator chooses, as today (§614). Where each of them made the database's
  first authority, each also made a store key, and the operator is asked which store to keep
  (§649).
- **A renewal approved at a gate that had not heard of a revocation is refused once the copies
  meet**, because a revocation names the client's key (§615).
- **A certificate taken away at a gate is refused at another** once that gate's KeePass takes
  the file in, and so is a renewal made from it there in the meantime (§648).
- **Gates need not run the same release.** What an older and a newer plugin do on the same
  store is §652.

Several gates were run as files that each write (`measured-store-as-a-whole` 2.4, 3.1). Two
KeePass instances with the plugin were not run (§644).

---

## 6. The store key §632

**What it is.** 32 random bytes, made with the first authority (the design paper 7). Every check
and every identifier of the store is keyed by it.

**No key is made under which two kinds' tags meet** (this document's own). Where the tags of two
kinds are the same under a key, a deletion record of the first kind reads as a deletion record
of the second (§623 step 2), which tells of more and never of less, and the plugin deletes
admissions as a matter of course (§627 step 3): under a key where the tags of `client` and
`revoked` meet, a client that renews and gives its earlier certificate up would be revoked for
good, and every client of that store with it. So the plugin draws the key again until the tags
of all kinds of the format differ from each other. Two of the 3-byte tags of the kinds of the
first format (§658) meet under a random key about once in 300,000, so a second draw is rare and
a third rarer still. A key found in a file is taken as it is, since the reading
cannot choose it. *Held by* `StoreFormatTests`: a key under which two tags meet, found by a
search run once, is drawn again.

**Where it lives.** In the protected content of every authority's entry, and of every
retirement that Delete or keeping an authority wrote (§614). While an authority is served, its
entry holds the key. After a Delete with no new authority, the retirement holds it. No record
of another kind, no other place in the file and no place outside it holds the key.

**Carried over a Recreate, kept in the retirement on a Delete** (decision 12). The key is not
renewed with the authority, so rules, profile names and revocations stay valid across both. A
copy from before a Recreate and a copy after it merged as decided in both directions by every
method but Create new IDs, and after a Delete with no new authority followed by a later Create
every rule was kept. Without the key in the retirement, everything under the key is passed over
after Delete and Create (`measured-store-as-a-whole` 2.4, 2.5, 3.1; `measured-decided-store` 2.1).

**The entry that carries the key is written again like any record** at the plugin's next own
write, and then survives 10 further edits. For the retirement that holds the key no authority is
served, so the plugin's next own write is Create, and that Create writes the retirement again.
After it the new authority's entry holds the same key, so a later loss of the retirement no
longer loses the key (`measured-two-readings` 2.4).

**Its loss.** Where the entry that carries the key has no version that passes its check, the file
holds no store key. Three things leave that entry so, each measured on real files
(`measured-decided-store` 3.5):

- **A deletion for good, with its deletion record.** The key is gone in the copy where the entry
  was deleted and, after Synchronize in either direction, in both copies. A KeePassXC merge
  carries no deletion record: merged into the other copy, that copy keeps the entry and reads as
  decided.
- **The 11th edit of that entry** under KeePass's default history limit, with no write of the
  plugin's own in between. The key is gone in that copy and in every copy the edited entry
  reaches, by Synchronize and by KeePassXC 2.8.0 in both directions, and no merge with an
  unedited copy brought it back.
- **The first edit of its content where the database keeps no history.**

**A lost store key is a store started again** (decision 19). Nothing is recognized as the
store's, nothing is served, the gate cannot write, and sealing the store again does not apply.
The next Create starts a new key, and makes a group of the plugin's own for it, also where the
group of the earlier store is still in the tree (§606): the entries of the earlier store stay
where they are, passed over and shown. Every client pairs again with the operator's approval,
and every rule, profile name and revocation written under the lost key is passed over and has
to be entered again.

**By itself the loss widens nothing.** Nothing is served until Create, and with every earlier
rule passed over the store reads narrower than before: in every measured cell after the
retirement's loss the root's rule was gone. A client reaches more than before only through what
the operator enters again (§641).

**The loss is accepted, with a warning.** It is accepted for the deletion, for the edits and for
the retirement as for the authority's entry. The key gets no second carrier and the loss gets no
further dialog. The entry that carries the key is marked as not to be deleted (S14), the KPG tab
reports a missing authority (§636), and the operator's documentation describes the loss as what
it is: a store to be entered again in full, its narrowing rules included.

**A command on the KPG tab starts the store again** (a later decision). After a warning that
names what has to be entered again, it makes a store under a new key and sets every other key
aside, and the earlier store coming back by a merge counts for nothing. Delete and Recreate do
not start a store again: Delete keeps the key in the retirement and Recreate carries it, so both
keep everything written under it.

- **What it writes** (proposed). A single write through the writer, applied at the settings
  dialog's OK like everything on that tab: a new store key; an authority that holds it, with its
  host certificate and its gate key; a version record with the plugin's version and format
  number (§650); a keep record under the new key for every other key found, each of which names
  in `after` every keep record its writer had read (§649); and the summary entry that restates
  them. It deletes nothing. The entries of the earlier store stay in the file, passed over and
  shown (S30).
- **What its warning names** (proposed). Every client has to be paired again. Every profile
  name, every rule, every revocation, every certificate taken away and every standing answer to
  a renewal has to be entered again, the rules that narrow among them. The change reaches every
  copy that takes the file in, and no command takes it back.
- **Who can use it.** A plugin that the store's format holds back, or that does not read the
  store's form, can use it, since it writes in the plugin's own form and reads nothing of the
  earlier store but its key. Nothing of the earlier store is converted.
- **The earlier store coming back by a merge counts for nothing**, and does not hold the plugin
  back again: its key is set aside wherever the keep record is, and the store's format number
  and the rule on a kind the plugin does not know are asked of the records under the store key
  alone (§651, §621).
- **A key the plugin cannot find, it cannot name.** Where the earlier store's key is in a form
  the plugin does not read, that store is no store to this plugin, and a plugin that finds both
  keys asks which store to keep (§649, reasoning).

**The path by hand remains possible.** With the guard switched off, the operator deletes the
plugin's group for good, synchronizes that deletion into every copy, and creates an authority.
It is the operator's own act on the file, and it sets nothing aside: an earlier store that a
merge brings back is then a second key, and the operator is asked which store to keep (§649).

**A lost key that comes back beside a new key is a second key.** Where a store was started again
and a merge then brings the entry that carried the lost key back, as a merge that carries no
deletion record does, the file holds two keys that each vouch for themselves, and the gate
serves nothing until the operator keeps a store (§649). The operator can then keep the store
that was lost (reasoning).

**What of a key the plugin overwrites, and what it cannot** (read from the product). A store key
the reading finds is held as bytes by the checked form of the version that gave it, and
overwritten when that form is let go: at a lock, at a close, and when the version leaves its
entry. Nothing else of a key is kept between two holds: what a form was asked is filed by the
key's name (§649), which tells nothing of the key. What cannot be overwritten is every string the
key passed through on its way to those bytes, as with the host key, `CA.md` §208: the content of
an authority's or a retirement's version, which holds the key in hexadecimal and will hold the
authority's private key, as KeePass hands it to a reader; the text the check is computed over,
until it is made into bytes; and the strings the JSON reader makes of the content's members.
Each goes with the collector, and none is kept by the plugin past the reading that made it.

**Who holds the key.** Whoever can open the database, or could open any copy of it made since
the first authority, since the key is never renewed (reasoning; §639).

---

## 7. The version of the plugin that wrote the store §647

What is decided here was decided in two steps, both later decisions, and is given in the words
of those decisions:

- **The plugin writes a record of the version of the plugin that wrote the store, from 0.3.0 on.
  With it the plugin can check forward and backward whether it works with the store, and only
  the highest written version matters.**
- **The version record holds the plugin's version and a store format number, and the format
  number decides.** It moves in the very change that alters the store's form. Only the highest
  of each matters. A patch release never changes the format number.
- **An older plugin on a store that a newer plugin raised serves nothing for that database**,
  and states why on the tab, in the log and to clients. There is no band in which it serves
  without writing, and no way back but a store started again.
- **Only the operator raises a store, with a button on the tab, after a warning that names what
  stops.** Until then the newer plugin writes nothing into the store: no write of a client and
  no write of the plugin raises the store or writes into it, and such a write gets an answer of
  its own.
- **A record that passes the store's check and is of a kind the plugin does not know stops the
  gate for that database.**

Everything else in this section is proposed: it was worked out for this document and none of
it was run. S26 lists what of it is still to settle.

**What it is for.** Some release will change what the store holds. A plugin from before that
release would read what it does not know as well as it can, and a plugin that writes into such
a store writes a summary entry that leaves out what it could not read, and deletes the summary
entry that held it (§627). The format number is how a plugin learns, before it serves or writes
anything, that the store has a form it was not built for. The rule on a kind it does not know
is a second safeguard that needs no version record (§621).

### What is recorded §650

**A version record** is a record of a kind of its own, under the check as every record is, in
the plugin's group. Its form is below (S23). In this document the
words name this record alone: a record of kind `version` is something else, a version of a rule
or of an answer to a renewal (§617), and a version of an entry is its current strings or an item
of its history (§621). A version record states two things: the version of the plugin that wrote
it, as the three whole numbers the build stamps the plugin with, and the format number of the
store as that plugin writes it.

**The format number is a whole number of the store's own**, apart from the plugin's version. It
decides what a plugin does with a store (§651), and the plugin's version decides nothing: it is
there for the operator to read, on the tab and in the log, which release wrote the store and
which raised it.

**The form of a version record, fixed with 0.3.0 for good** (S23). Every plugin from 0.3.0 on
has to read a version record that any later plugin wrote, so this form never changes, whatever
else a later format changes, the check and the identifier of other records among it.

| Part | Form |
| --- | --- |
| `KPG.Kind` | `format` |
| `KPG.Data` | `format`, the format number, a whole number from 1 to 65,535 in decimal with no sign and no leading zero; `plugin`, the plugin's version as three whole numbers in decimal joined by periods, as `0.3.0`; and `nonce` |
| `KPG.Mac` | the check of §608 |
| Identifier | as §609, with the subject `format:` and the format number in decimal, and the format number in bytes 9 and 10 |
| Its item in a summary entry | `{"format":"1","kind":"format","plugin":"0.3.0"}`: the highest of each that the store states |

It is not protected. A reader passes over a further member (§607), which is how a later format
adds to it. The number is in the identifier twice, in clear and under the key, so that a
deletion record of a version record states its format number to a plugin that has never heard
of that format (§623). **A later format that changes how a store key is found** leaves an
earlier plugin with no key: it then reads no version record, finds no store and serves nothing
(§632), which is the same refusal by another road.

**The first format number is 1**, and it is the form release 0.3.0 writes. Until that release,
every build states 1 while the form is still being built, as §658 has it for the kinds.

**What alters the store's form**, and so moves the number (proposed):

- a new kind of record, or a new kind of item in the summary entry;
- a new member that a reader must not ignore, or a member whose meaning changes;
- a change to the check or to the identifier: what the check covers, how it is computed, what
  the identifier is made of;
- a change to what the reading takes from a record a plugin already writes, or to what a write
  deletes or restates.

A text that nothing reads does not alter it, and neither does a member that an earlier reader
may ignore without reading anything else differently. So a reader passes over a member it does
not know, in a kind it knows (S26, §607).

**Where the number is written down** (proposed). A single constant in the plugin's source,
beside the code that writes a record, and nowhere else. It is not taken from the release's
version, which is written once for the whole product, `CONVENTIONS.md`, and moves only when a
release is made, `RELEASING.md`. A build made between two releases therefore carries the last
release's version, and where it changed the store's form it carries a format number of its own.
That is what tells such a build apart from the release whose version it still states.

**A test pins it** (proposed). The test holds the number beside a description of what the writer
emits: each kind, each kind's members, each kind of item, what the check covers and how the
identifier is laid out. A change to what the writer emits fails the test until the description
is changed with it, and the description is changed together with the number or with a stated
reason why the form is the same. That puts the judgment in front of whoever makes the change,
in the change that makes it.

**The form of that description** (S26). It is a text written out in the test `StoreKindsTests`,
beside the number: a line for the text the check covers (§608), a line for the layout of an
identifier (§609), a line for each ceiling (§607, §643), and a line for each kind, in the order
of §658, that gives its name, its subject, what bytes 9 and 10 hold, each member the core
reads with its form, what a deletion record naming it does, its item with the item's members,
and for `version` each type of object. The test makes the same text from the table the code
reads by and from the code's own constants, and holds the two to each other character for
character. A pull request that brings a kind's meaning adds that kind's further members to its
line. So the description is not a second implementation: it is the table as a person reads it,
and it cannot move without the test's own text moving in the same change.

**A patch release never changes the format number.** A fix that has to change the store's form
is therefore a minor release, which by `CONVENTIONS.md` is also what a change the operator has
to act on is (reasoning).

**A plugin that cannot state both writes nothing.** A plugin whose own version does not read as
three whole numbers, or whose build states no format number, writes nothing into any store
(proposed). A version record that does not state both does not pass.

**When a version record is written** (proposed).

- **With the store's first write**, the Create that starts a store, in the same write as the
  authority. It states the plugin's version and format number.
- **When the operator raises the store** (§652). It states the raising plugin's version and
  format number. This is the only write that states a higher format number than the store's.
- **With a write of the plugin's own, where the plugin's version is above the highest the store
  states and its format number is the store's** (S26). That record states the same format
  number and holds no plugin back. It is what keeps the record true to "the version of the
  plugin that wrote it", which is what the decision asked of it, and it is what lets the tab and
  the log name the release that last wrote. It costs an entry for each release that writes into
  a store, and a further entry where two gates of that release each write before their copies
  meet, and the summary entry's single item does not grow by it. It rules out a record for
  every write, which would be an entry for each, and no record until the format moves, under
  which a store would name only the release that made it. A plugin whose version is not above
  the highest the store states writes none. *Held by* `StoreWriteTests`, with the first write:
  a write by a plugin of a higher version adds the record, a second write by it adds none, and
  a write by a plugin of a lower version adds none.

**Every summary entry restates the highest format number and the highest plugin version** the
store states at that write. So each has two carriers, as everything has whose loss could end
wider (§611).

**A version record is never replaced and never deleted by the plugin.** It names nothing in
`after`, and the records of lower numbers stay beside the highest. It counts whether or not a
deletion record names it, and a deletion record that tells of a version record counts as that
record does (§623).

**The highest of each** is the greatest value stated by a version record that counts, by a
summary entry present, or by a deletion record that tells of a version record. A version record
counts as any record does: under the store key, in its current version or by its history,
wherever it sits. A greatest value needs no clock and no memory of copies. Every merge that
brings both copies' records together gives the same answer in each copy, and no order of merges
changes it (reasoning). An import with Create new IDs brings no record together, since what it
brings are copies (§640).

### What the plugin compares §651

The plugin compares its own format number with the highest format number the store states.

| The plugin's format number is | The plugin |
| --- | --- |
| the store's | reads, serves and writes |
| below the store's: an older plugin on a store that was raised | serves nothing for that database, and writes nothing |
| above the store's, and the plugin reads the older form | reads the store as it is and serves. It writes nothing into the store until the operator raises it |
| above the store's, and the plugin does not read the older form | serves nothing and writes nothing. The operator starts the store again (§632) |

The rule on a record of a kind the plugin does not know stands beside this table and does not
depend on it (§621).

**Considered and not taken: the writer states from which version on its store may be read and
written.** An earlier form of this section had every version record carry two plugin versions,
the lowest that reads what the writer writes and the lowest that may write into its store, with
a band between the two in which an earlier plugin served and did not write. It is not taken, for
these reasons. Both values were versions of the plugin, and a build made between two releases
carries the last release's version, so a build that changed the store could neither state its
own values nor be told apart from the release it claimed to be. A patch could not raise them, so
a release found wrong in what it writes could not be fenced off. The values asked a release to
judge what every earlier reader does with what it writes, and a value set too low ends wider. A
gate in the band could not revoke. And the first write of any kind by a newer plugin, a client
giving a certificate up among them, held every earlier gate back with nobody having decided it.

### What an older and a newer plugin do §652

**An older plugin on a store that was raised serves nothing.**

- **It tells why.** The KPG tab states the format the store was raised to and the version of the
  plugin that raised it, beside the plugin's own, and that the plugin has to be updated. The log
  has a line when the plugin first sees the state: when the database is opened or unlocked, and
  at the save that ends a synchronize. A connection is answered with a word of its own, in the
  manner of `503 undecided` (§656).
- **It writes nothing**, and no command of the tab is an exception but the command that starts
  the store again: not Create, not sealing the store again, not keeping a store that was raised
  above it (§649).
- **There is no way back but a store started again.** The highest format number never comes
  down, and nothing a plugin writes lowers it. Deleting the version record that raised the store
  is no way back: the deletion record tells of it and counts as it did, and a merge from a copy
  that was not cleaned brings the record itself back (§623).
- **The store is started again with the command on the tab** (§632), which the older plugin can
  use: it makes a store in that plugin's own format under a new key and sets the raised store's
  key aside, so the raised store no longer holds the plugin back, also where a merge brings it
  back.
- **A gate on a stale copy does not know yet.** A gate with an older plugin, on a copy that the
  raising record has not reached, serves and writes as before. What it writes was made apart
  from the newer plugin's writes and comes together with them as the writes of any two gates
  do. Once its KeePass takes the file in, it serves nothing (reasoning).

**A newer plugin on an older store reads it as it is, or refuses it.** Nothing is migrated: no
code converts, upgrades or reads kindly what an earlier release wrote, by decision 17 and by
the rule of §645.

- **A release that reads the older form reads it as it stands**, every record in it, and serves.
  A store keeps what earlier formats wrote for as long as it lives, since nothing is converted,
  so a release that reads a store of a format reads everything that such a store can hold.
- **A release may equally refuse every store written before it.** While the version begins with
  0, a release that changes a form owes an earlier store nothing, by the same decision and the
  same rule. It then serves nothing from such a store, the tab states which format the store has
  and that this plugin does not read it, and the operator starts the store again, as above.
- **Until the operator raises the store, the newer plugin writes nothing into it.** It adds no
  record and no summary entry, sets no edited record back (§628), and writes no rule from a
  node's dialog (§629). A pairing, a renewal, a certificate given up or taken away, a
  revocation, a profile name, a rule and every command for the authority are refused, to a
  client with a word of its own (§656) and on the tab with the reason. A client's change to the
  operator's own entries is served, since it writes nothing into the store (reasoning).
- **The operator raises the store with a button on the KPG tab.** Like everything on that tab it
  is applied at the settings dialog's OK. Before it, a warning names what stops: every KeePass
  that serves this database with an earlier KeePassGate serves nothing for it once the change
  reaches its copy; nothing takes the change back but starting the store again, with every
  client paired again and every rule entered again; and the other machines are to be updated
  first. The plugin cannot name those machines, since nothing in the file names a running
  plugin.
- **What the button writes.** A single write through the writer: a version record that states
  the plugin's version and its format number, and a summary entry that restates both. From then
  on the plugin writes in its own form.
- **Release 0.3.0 has no older store**, since the store begins with it. No store states a
  format number below 1, so no state of a 0.3.0 plugin offers the button or refuses a write for
  it, and code that nothing can reach is not built. **The release that first moves the format
  number builds the button, its warning and the refusal** (S26, S16): it writes the caption
  and the words of the warning, held to what the warning has to name above, and it refuses the
  requests §656 lists. What 0.3.0 fixes is what a client has to know by then: the word and the
  requests it answers.

### What the version record can do to access §653

**It makes nothing count that would not count without it.** A version record names no client, no
key, no authority and no node, and no step of the reading admits, grants or lifts anything
because of a version. Its whole effect is on the plugin that reads it: that plugin serves
nothing, or writes nothing. So the worst a version record does is refuse (reasoning).

- **A forged record** needs the store key, like any record. **A copy** of a version record under
  another identifier is passed over (§621). **A record under another key** is under a second
  key, which stops the gate until the operator keeps a store, and a record under a key set
  aside counts for nothing (§649).
- **A record edited by hand** is read under the history rule as any record is (§622): it counts
  by the version the plugin wrote while the history holds that version, and the plugin's next
  write sets it back. With no version that passes it is damage and stops the store, and the
  summary entry still states the highest of each.
- **A record deleted for good** changes nothing: the deletion record tells of it, and the
  summary entries state the highest of each (§623).
- **A record removed with no trace, with every summary entry that states it**, leaves the store
  stating less than was written. An older plugin is then held back only by what the store still
  holds of the newer form: a record of a kind it does not know stops it (§621), and a form that
  changed inside a kind it knows does not (§639).
- **A format number above every plugin makes every plugin refuse**, for good. A file from
  somebody who does not hold the database's key cannot do it: its record is under another key.
  What remains is somebody who writes under the database's own key (§639), and an operator who
  raises a store with a build that no other machine has. It is a refusal and never wider, and
  the only way on is a store started again (§652).
- **A build that is no release** carries the last release's version. Where it changed the
  store's form it carries a format number of its own, so the released plugins tell its store
  from theirs. It states that number in a store only where it starts the store or where the
  operator presses the button.
- **An older file put back** states what it stated then and holds none of the later records, so
  what it states is true of it. It is accepted as any older file written over a newer file is
  (§639).

### What it cannot reach: a 0.2 plugin §654

**No record reaches a plugin that was released before the store.** Release 0.2.0 knows nothing
of the store or of the version record. It serves every group and entry of a database by its own
rules for the place where each sits.

**A 0.2 plugin on a database that holds the store is answered by a warning, and by no code** (a
later decision). There is no release 0.2.1 for it, and nothing in 0.3.0 looks for a 0.2 plugin.
The warning stands in the release notes of 0.3.0 as a breaking item and in `docs/`, as a short
section "Moving from 0.2" on the getting-started page. Neither is written by this document:
§645 states which pull request writes each. It names version 0.2 and the steps of the move,
and describes nothing of how the store of 0.2 worked. §645 has the steps and their order.

**What a 0.2 gate serves of the store** (read from the source of 0.2.0, not run). It takes a
machine that still runs 0.2 on the same synchronized file, or a plugin put back to 0.2, and a
client that 0.2 still admits, with a rule that reaches the plugin's group. That group is an
ordinary group to 0.2 and inherits the rule of the root.

- **With `read` there**, the client is served every record but the authority's content: who is
  admitted, which names exist, what each rule grants.
- **With `read` and the `protected` switch**, it is served the authority's content, which holds
  the authority's private key, the host key, the gate key and the store key.
- **With `update` there**, it can change or strip the plugin's fields. Eleven updates of the
  authority's entry lose the store key, and the store is started again (§632).
- **It cannot plant a record.** An entry that the 0.2 API creates gets an identifier of
  KeePass's choosing, and a record counts only under the identifier its content gives (§609).
  With the store key and `update` together it can search for a content that fits an existing
  record's identifier and write it in place, which the six bytes of §609 put at about 2^48
  tries (reasoning).

That is fail-open (§639). **Granting on groups and not at the root closes it**: the plugin's
group then inherits nothing, and a 0.2 gate serves nothing of it. The warning carries that
advice beside the steps.

### Upgrades, as the operator meets them §655

Each is what follows from the sections above, step by step, and none was run.

**A single machine, and a release that leaves the format alone.** The operator installs the
release. Nothing is asked and nothing is shown that was not shown before. The plugin reads,
serves and writes.

**Two machines, upgraded a week apart, and a release that leaves the format alone.** Both serve
and write throughout the week, the upgraded machine and the other alike.

**A release that changes the format.**

1. The operator installs it on a machine. That plugin reads the store as it is and serves. It
   refuses every write into the store, and the tab states why and offers to raise the store.
2. The operator installs the release on every other machine that serves the database, and
   synchronizes.
3. On a single machine the operator raises the store, reads the warning, accepts the dialog,
   saves and synchronizes. Every machine then reads, serves and writes.
4. Where a machine was left on the earlier release, it serves nothing for this database once
   its KeePass takes the file in, and its tab, its log and its answers to clients state why.
   Installing the release there is the remedy.

**A release that does not read the earlier form.** After the install the plugin serves nothing
for the database, and the tab states that the store has to be started again. The operator
starts it again with the command on the tab, past its warning (§632), pairs every client again
and enters every rule again.

**A plugin put back to an earlier release.** Where the store was not raised, the earlier plugin
works as it did. Where it was raised, the earlier plugin serves nothing, and nothing brings it
back but the later release, or the store started again with the command on the tab (§632).

---

## 8. The API and the store §633

The store is made of ordinary entries, and the API serves entries. Today no route reaches custom
data, so the store is out of reach by construction. In entries it is out of reach only if the
API is built to keep it there.

**What an entry of the store is, for the API.** An entry under an identifier of the store, which
the keyed tag lets the gate recognize, or a copy of a record whose content passes the check. An
entry is not the store's for carrying a `KPG.` string. The store's reading passes a copy over
(§621), and the API hides a copy all the same, since a copy of the authority's entry holds the
authority's private key and the store key. For the API a key is every key found, whether it
stands or was not kept (§649), since an entry under a key not kept can hold an authority's
private key as well.

**Absent on every route.** The plugin's group; every entry of the store, wherever it sits, the
recycle bin included; and on any other entry every field whose name begins `KPG.`. Absent means:
in no listing and no sweep, not readable, left out of the tag a client compares, not offered by
the routes that tell a client what it may do at a node, and answered like a node that is not
there. The reference client follows field references through these same routes, so it cannot
follow a reference to what they do not serve (reasoning).

**Refused on every write.** Creating, renaming, updating or stripping a field whose name begins
`KPG.`, on any entry; any change to an entry of the store or to the plugin's group; and deleting
a group that holds an entry of the store. That last refusal is the answer the product gives today
for a group that holds the hidden recycle bin, and like it, it tells a client that something it
cannot see is there.

**The authority's private key in an ordinary entry is conditional** (decision 8). It is allowed
only if a test of every route shows that the store cannot be reached through the API. **That
test is `StoreOnEveryRouteTests`**, in the host tier, and a failure of it reopens the decision.

**What is built.** The guard sits where the recycle bin's absence is decided, in the vault's own
lookups and listings, `DESIGN.md` §118 and §138, so the router finds no node for a node of the
store and asks the rules nothing about it, and no handler is handed such a node. What the vault
leaves out:

- **A group whose identifier begins with the prefix of §606**, with everything inside it,
  wherever it sits. Every such group, since which of several is written into is S11.
- **An entry that is a record by its identifier**: its first three bytes are the tag of a kind
  §607 names, under any store key the file holds. A key is found as §621 finds it, in a version of
  an authority or a retirement, current or in the history, that passes under the key it holds and
  sits under the identifier its content gives, anywhere in the tree, the recycle bin included. For
  the API a key is every key found, as the paragraph above has it. Nothing else of the entry is
  asked, so a record that the reading passes over, damaged, stripped of its strings, or under a key
  the operator did not keep, is absent all the same.
- **An entry with a version that passes its check under any key found**, whatever its
  identifier: a copy, and a record of a kind the plugin does not know, which no tag tells.
- **On every other entry, every string whose name begins `KPG.`**, compared byte for byte and as
  the entry's own dictionary compares names, so a spelling that collates to such a name is such
  a name as well. Such a string is in no view, so in no answer and no tag; changing or stripping
  such a string is answered as a string the entry does not hold is, and creating such a string is
  answered `404`, whether or not the entry holds it.

**What follows from telling a record by its tag.** An ordinary entry of the operator's whose
identifier begins with a tag is absent too: a random identifier meets a given tag about once in 17
million entries (§621), and the tag of some kind of §658 under a single store key as many times as
often as the table has kinds.
Where the file holds no key, nothing outside the plugin's groups is told as the store's, and a
record there reads as an entry holding a title and notes, every string of the store left out.

**Hiding asks nothing of what the reading makes of a record.** A record stripped of all its
strings, which the reading treats as a deleted record (§621), is absent by its tag for as long as
the key it was written under is found. A record of a kind the plugin does not know, which stops
the gate (§621, §652), is absent by its content passing the check, whether or not the gate has
stopped: the stop belongs to the reading, and hiding does not wait for it.

**The formats it takes as stated, and where each is open.** The prefix's eight bytes, as measured
(§606). Every kind of §658, each of which a tag tells. Six bytes of the check in an identifier
(§609), and two bytes for a number. The members `id`, `authority` and `storeKey`, the key as 64
lowercase hexadecimal characters, S3. The reader of `JsonBody` for a content, with its ceilings
(§607). Each is written once in the code, so a change to it is a change in a single place.

**It takes its keys from the store's reading** (§620), which the hold the lookup is made in has
made once: the tags of every kind under every key found, and the entries with a version whose
check passes under any of them. The guard walks nothing of its own.

**The cases the test drives** are the ways a route names a node, and each operation the routing
document declares has a case, read off the document: no node, for the gate itself, the caller's
certificate and pairing; the whole tree, for the two listings anchored at the root; a group, by
the identifier in the path; an entry, by the identifier in the path; a string, by its name in the
path or in a body; and an attachment, by its name in the path. No body names a group or an
entry and no query names anything on any route today, and the test refuses an operation with no
case, and a body member or a query name it does not know, as the router refuses an operation
with no handler, so a route added later is held here before it ships. Each way is driven with
every node of a composed store: the plugin's group, a group inside it and a second group with the
prefix elsewhere, records inside the group, in an ordinary group, in the recycle bin and under a
second key, a damaged record, a stripped record, a copy of the authority's entry under another
identifier, a record of a kind the plugin does not know, and strings of the store on an ordinary
entry; with and without `If-Match`, and with and without each query name declared; by a client
whose rule at the root holds every verb and both switches. Each answer is held, byte for byte
with its headers, to the same request about a node that is not there, every answer about an
ordinary node and every listing to its answer before the store was added, and the store to its
state before the request.

**A group that holds an entry of the store is refused its removal** as a group holding anything is,
which the vault already answered, since it refuses a group that is not empty.

**The recycle bin named as the plugin's group is unsupported** (§637). A node a client removes is
then moved into the plugin's group, as a removal moves it into the bin, and is absent for every
client from then on, as a node in the bin is. No record is touched. The operator is advised not
to name the plugin's group, or a group inside it, as the recycle bin. `StoreOnEveryRouteTests`
holds the removal of an entry in that arrangement.

**What the test does not show.** A lookup of a node of the store takes the time a lookup of a
node that is there takes, and not the time of an identifier nothing holds, as for a node in the
recycle bin and a node the rules refuse: the API answers those alike in what it sends and not in
when, `API.md` §401, and this guard closes nothing more. No end to end run carries a store, and
the records are composed by the test in the form the store was measured with, not written by the
plugin. The reference client follows a field reference only through `readEntry`, which the test
holds, so it cannot reach what that route does not serve (reasoning).

**What the API cannot rule out is its own defect.** A single route that forgets the guard serves
the authority's private key and the store key to a client, which can then make a record for
itself that passes the check. In custom data a forgotten route exposes nothing. That is fail-open
(§639).

**Outside the API** the key is as visible as any protected string to whoever holds the open
database: the entry view shows a protected string masked, the entry dialog lists it masked with
its Edit and Delete buttons enabled, and KeePass's XML export writes protected strings in clear
(`measured-desktop-windows` 4.3). That is the person at the desk, whom the threat model concedes,
`CA.md` §220.

### What a client is answered while the gate holds back §656

**Each state in which the gate holds back answers a client with a word of its own** (a later
decision): a word for each reason. **The five words stand, with these meanings** (a later
decision), and they are part of the contract.

| The state | The word | Answered to |
| --- | --- | --- |
| The store is stopped (§624), or has no room for its next write (§643) | `stopped` | every request, until the store is sealed again or, where it has no room, started again |
| Several store keys stand (§649) | `split` | every request, until the operator keeps a store |
| The store was raised above the plugin, or holds a record of a kind the plugin does not know (§652, §621) | `outdated` | every request, until the plugin is updated or the store is started again |
| The file holds no store key (§632) | `lost` | every request that the gate can still answer (below) |
| The store waits to be raised (§652) | `frozen` | a request that needs a write into the store, and no other |

**They are made as today's are.** `locked`, `undecided` and `busy` each name, in a single word,
the state that refuses. Like those, each is sent under `503`, in the body's `error` and in
`KPG-Unavailable`, and is described in the `Unavailable` answer of the routing document.
`undecided` stays what it is, several authorities under a single store key (§614).

**A store raised above the plugin and a record of a kind the plugin does not know share a
word.** To a client the two are the same state with the same remedy, and the remedy is not the
client's: the plugin on that machine is older than what wrote the store, and has to be updated.
The rule on a kind the plugin does not know is a second safeguard for the very case the format
number covers (§621). The tab and the log tell the two apart for the operator, who is the person
who can act on the difference.

**`frozen` is the only word that not every request gets.** A gate with a newer plugin on a
store that was not raised serves, and refuses what needs a write into the store: a pairing, a
renewal and a certificate given up (§652). A read is served, and so is a change to the
operator's own entries.

**The requests `frozen` is answered to** (S16) are those of the operations `pairHello`,
`pairRequest`, `renewCertificate` and `revokeCertificate` of the routing document, and of no
other operation. Those are the requests through which a client writes into the store (§627):
no other route writes a record, since a client's change to a group or an entry adds nothing to
the store, sets no edited record back, and deletes no rule with the node it removes. A pairing
is answered at its hello and at its request alike, before the operator is asked and before the
invitation is spent, as every `503` of a pairing is today, `PAIRING.md` §311, and a renewal
before its dialog. It rules out answering `frozen` to every route that changes something,
which would refuse a change to the operator's own entries, and it rules out leaving the hello
served, which would have a client ask the operator for a pairing that cannot be written. A
route added later that writes into the store joins the list in the change that adds it. **A
gate of release 0.3.0 never answers it** (§652). *Held by* the test that the release which
first moves the format number writes, over every operation the routing document declares: the
listed operations answer `frozen`, and every other operation answers what it answers on a
store of the plugin's own format.

**A database in several of these states is answered the first of them in this order**
(S33): `locked`, as today, since nothing of a locked database can be read; `lost`; `split`;
`outdated`; `stopped`; `undecided`; and `frozen`, which is asked only once a request is
otherwise served. The order is the order in which the reading can know each: without a store
key nothing else can be asked, without a single standing key no record counts, a store raised
above the plugin is not judged whole or stopped by a plugin that does not know its form, and
which authority is served is asked only of a store that is whole (§614). *Held by*
`StoreWordsTests`: a store in two of the states answers the earlier word, for each pair.

**Where the words become part of the contract.** In the routing document, in the pull request
that makes the gate answer them. `API.md` §411 counts a status and its word on the wire as the
contract, and a word a client may rely on moves the minor version, which is 0.3.0. The reference
client gets a class for each word in the same pull request, as it has for each word today. A
0.2 client takes each for the gate being unavailable (§645). `frozen` is declared with the
words the core answers, and its description states that a gate of that release never sends it,
so that a client built for 0.3.0 knows the word when a later gate sends it.

**Which certificate a word is answered with, and when no word can be sent** (S33). An answer
takes a host certificate and a gate key, and both are in an authority's record. The rule is
that of `undecided` today, for every word:

- **A word is answered under each authority that stands**, with that authority's own host
  certificate and under its own gate key: an authority whose record passes under a key that
  stands, is not beside a deletion record naming it, and is named by no retirement under that
  same key (§614). For `stopped` and `outdated` that is the authority the store would serve,
  or each of several. For `split` it is each authority that stands under each key that stands,
  so the clients of every store reach the word, as the clients of every authority reach
  `undecided`. No client certificate is judged on such a connection, and nothing is read or
  written for it.
- **Where the reading finds no authority to answer with, the gate answers from what it held
  when it last served that database**: the host certificate and the gate key of the authority
  it was serving, kept in memory since, and let go at the lock, at the close and when KeePass
  ends, as the host key is today, `CA.md` §208. That is the only way `lost` reaches a client:
  a gate that was serving when the key was lost, by a synchronize that brought the deletion or
  by the edits in its own window, answers `lost` until the database is locked or closed. It is
  also how `outdated` is answered where a later format changed the authority's record so that
  an earlier plugin reads no host certificate in it. Where a key that stands retires the
  authority the gate held, as another copy's Delete brings a retirement in a synchronize, what
  it held is let go and the name resolves to nothing, held back or not, since a retired
  authority's clients are told nothing. A connection that took what the gate held before the
  store came to hold nothing back is answered `busy`, and asks again.
- **In every other case the name resolves to nothing and the connection is dropped with no
  answer**, as for a database with no authority today, `DESIGN.md` §112: a database opened or
  unlocked with no store key in it, a KeePass started after the key was lost, and a database
  that never held a store. The responder a locked database answers with is not used for
  another word, since a client that meets it takes the database for locked before it sends
  anything, `CA.md` §208.

So every word can reach a client, and `lost` reaches a client only from a gate that was serving
when the key went. It rules out minting a certificate to answer with, which needs the
authority's key and would be a choice between authorities where several stand, and it rules out
answering under a retired authority, whose clients are told nothing today. *Held by*
`StoreWordsTests` and `GateListenerTests`: each state answers its word over a real handshake
with the standing authority's host certificate, to a request before a route is chosen and to a
`head`; a database opened with no store key resolves no name; and a gate that was serving
answers `lost` after the key carrier is deleted under it, and nothing after a lock.

**What is built.** `split`, `outdated` and `stopped`, the last also for a store that has no room for
its next write (§643), answered before a route is chosen by the target of a database the gate
serves, and, ahead of `undecided`, by each target of a database that holds several authorities,
under the host certificate of the authority that stands or of each of those authorities, worked out
under each key that stands (§614). `lost`, and the memory of what a gate last served with: the host
certificate and the gate key of the authority served, copied as the target is made, carried by every
entry made for the database in its place, and zeroed at the lock, at the close, when KeePass ends,
and where a store that holds nothing back gives no authority to answer with, as where the authority
served was deleted on the tab, and where a key that stands retires that authority, held back or not.
Where the reading gives no authority to answer with and the store holds the gate back, that memory
answers the store's word, `lost` for a file with no key, under its own gate key, and `busy` once the
store holds nothing back, to a connection that took it before the reading changed; it judges no
certificate and reaches nothing, and a lock lets it go with no responder. A database opened or
unlocked with no store key, and a database whose store holds no authority that stands, resolve no
name. **A target handed out while a single authority stood answers `undecided` once a second
authority stands**, as a connection made then is answered: a request in flight, or waiting its
turn on KeePass's thread, when the second authority arrives is answered `503 undecided` where
its caller is judged again, and so is a pairing's, a renewal's and a given up certificate's
write (§627). **And only while that target's own authority is among the authorities standing**:
where it went while the others came to stand, a request in flight is answered as a certificate
with no admission is, and the memory of what was last served answers `busy`. So does a target
made for an authority that stood among several, once that authority no longer stands among
several when a connection that took the target asks: retired while others stand, or once the
operator has kept an authority. Each authority that stands answers `undecided` under its own
host certificate, so a client of an authority that is gone finds no target on a new
connection: its handshake is refused, while several authorities stand and after the operator
has kept an authority. The words are answered to such a write as well, `stopped` also to a
certificate given up that the store has no room for. A line that a database is served again,
and the warning from half the room, are logged only where the reading serves, so not while
several authorities stand. `frozen` comes with the release that first moves the format number
(§652), and the routing document declares it and the reference client knows it already. *Held
by*
`LockInRealDatabaseTests.A_candidate_whose_own_authority_went_while_several_others_stand_is_answered_busy`,
`HeldBackGateTests.A_candidate_whose_own_authority_no_longer_stands_among_several_is_answered_busy`,
`OpenDatabaseTargetsTests.A_connection_naming_the_gate_of_an_authority_that_no_longer_stands_finds_no_target`,
`LockInRealDatabaseTests.A_connection_made_before_a_second_authority_stood_is_told_undecided_as_a_new_connection_is`,
`LockInRealDatabaseTests.What_a_database_was_served_with_answers_undecided_once_it_holds_several_authorities`,
`LockInRealDatabaseTests.A_connection_whose_authority_went_while_several_others_came_to_stand_is_answered_as_its_authority_went`,
`LockInRealDatabaseTests.What_a_database_was_served_with_answers_busy_once_its_authority_went_while_several_others_stand`,
`StoreInRealDatabaseTests.A_stop_that_ends_with_several_authorities_standing_logs_nothing_as_served`,
`StoreWordsTests`,
`StoreHeldBackGateTests`, `HeldBackGateTests`, `UnavailableWordsTests` and `StoreInForceTests`, the
order of the words by `StoreReadingStatesTests` and `StoreRoomTests`, `lost` by
`LockInRealDatabaseTests` as well, where a served database whose key carrier is deleted answers
`lost` with the host certificate and gate key it served with, and nothing once locked and unlocked
or closed and opened again; a database whose authority was deleted answers nothing, also once the
key goes with the Delete's retirements; an authority retired into a stopped store answers nothing;
an authority whose record does not read in a stopped store answers `stopped` from the memory; and
the memory carried through an undecided database answers `lost`. `stopped` is held in a real KeePass
by `LiveStoreTests`.

---

## 9. In KeePass's window §634

**No hiding** (decision 16). The plugin never removes its group or its entries from KeePass's
tree or list: the group is visible and clearly named everywhere. Hiding was measured and is not
taken. With the group's node and the entries removed from the window, the Last Modified list
still showed every entry of the store, a field reference in an ordinary entry still copied a
store entry's user name and password, and the status bar still named the group; where the
removal ran at the window's own update event, KeePass's thread threw
(`measured-hide-and-lock` 2.3, 6.1). Placing the group under the recycle bin let Empty Recycle
Bin delete the whole store with a single question (`measured-hide-and-lock` 2.4).

### The guard §635

**A guard, on by default, with a setting on the KPG settings to turn it off** (decision 16),
through KeePass's public members only. It is a convenience against accidents and protects
nothing: the history rule and the summary entries remain the protection.

**The switch is in the plugin's window under Tools** (a later decision). It is kept in KeePass's
configuration on that machine, as the port is, and switching it writes nothing into any
database. So it is a setting of the installation, the same for every database that KeePass
opens, and a database carries no trace of whether its guard was on.

**What it does**, as measured in KeePass 2.62 with a probe plugin
(`measured-hide-and-lock` 3.2, 4.2, 6.2):

- **The entry dialog's save is canceled** for an entry of the store, through
  `PwEntryForm.EntrySaving`, the dialog's public event that precedes a save, subscribed when
  `GlobalWindowManager.WindowAdded` announces the dialog, with a short notice. Enter, and "Yes"
  to KeePass's question whether to save the changes before closing, each left the entry unchanged
  and the dialog open. A disabled OK button does not do this: answered "Yes", the save question
  saved the edit anyway.
- **The commands that change a node are disabled** while a node of the store is selected, in the
  Entry and Group menus and the context menus. KeePass enables them again at every update of its
  window, and the plugin, whose handler runs after KeePass's, disabled them again at each of 51
  updates. A disabled command's shortcut did nothing; a command that was only hidden still fired
  from its shortcut.
- **The keys that bypass the menus are swallowed** while a node of the store is selected: Delete,
  Shift+Delete, Alt+Down, the paste shortcut, and select all followed by Delete.
- **A drop is refused**, judged by the selection, since KeePass's drag names no entry.
- **Empty Recycle Bin is disabled** while the bin holds an entry of the store.
- **Deleting or moving the plugin's group is blocked as a command.**

**What it rests on.** The dialog's event and the window manager's event are public. The controls
and the menu items are reached by their names in KeePass's own window through public lookup
methods. Those names are not an interface, so each is pinned by a test, as `KeePassContractTests`
pins what the plugin reads of KeePass today. KeePass's own read-only view of an entry is not
used, since it needs a private field.

**The group dialog is not guarded** (decision 22). The group dialog tells a plugin which group it
is about through a private field only (`measured-hide-and-lock` 3.1). So the guard cancels no
save of a group dialog and does not judge a group dialog by the selection in the main window.
The group is found by its identifier and a record counts wherever it sits, so a new name or new
notes lose nothing, and the group's two flags, searching and auto-type, can be switched on there
(both reasoning; a change made in that dialog was not run).

**What the guard cannot cover** (`measured-hide-and-lock` 6.2; `measured-desktop-windows` 5.2,
6.2, 6.3):

- XML Replace, Database Maintenance, Delete Duplicate Entries, Delete Empty Groups and Import,
  before each of which nothing is raised that a plugin can see;
- Synchronize, which is announced to a plugin before the merge only while KeePass's trigger
  system is on, and which the guard does not stop;
- a command on a selection that holds the store without being a node of it: sorting the root's
  groups moved the plugin's group, and a delete or a duplicate of a group above the store takes
  the store with it (not run);
- the group dialog of the plugin's group;
- copy and paste of an entry of the store, which makes a copy under a new identifier;
- reading and copying every value, a key the guard does not list, a command another plugin adds,
  and every other program.

No public event precedes a delete, a move, a duplicate, a paste or a quick edit: the plugin first
sees each at the next update of the window, with the database already changed in memory. The
guard as product code, with its setting, was not run (§644).

### The KPG tab §636

- **It lists what the plugin's own group holds** (decision 5), and what is in force: the clients
  admitted with their certificates, the revocations, the certificates taken away, the profile
  names and the rules. That list is what the operator checks before sealing the store again
  (§625).
- **Its clients page takes a client away, or a single certificate** (§615, §648). Built: the
  rows grouped by client key, the X and **Revoke** that revoke the client, and **Take
  certificate away** in a row's menu, which takes that row's certificate alone, held by
  `ClientListTests`, `ClientListMenuTests` and `AuthorityPanelTests`. **A revocation or a
  certificate taken away that waits for OK leaves inviting as it was**: the invite button is
  turned off, with its notice, only while an act of the authority's waits, a Create, a Recreate
  or a Delete, since a code made then would pair against an authority the gate does not serve
  yet or is about to lose. A client revoked on the page, or a certificate taken away there,
  changes nothing about which authority a code pairs with. *Held by*
  `AuthorityPanelTests.A_revocation_waiting_for_OK_leaves_inviting_as_it_was`.
- **Its Profiles page adds a name and takes a name off the list** (§616), each an act of the
  store that waits for OK (§627). Built: the row that adds a name, which refuses a name where
  no store key stands and states that the database has no certificate authority yet; the X on
  each row; and the line stating that OK will not add a name the last press added, in the form
  for a name removed in another copy while the dialog stood and in the form for every other
  cause (§627). *Held by* `ProfileListTests`, `DatabaseProfilesTests` and `GateTabFitsTests`.
- **Its Profiles page counts, for each name, the nodes that hold a rule for it** (this
  document's own; §644 item 25): the groups and entries of the tree, outside the recycle bin
  and outside the plugin's store, at which a row for that profile stands. An empty rule counts,
  a rule taken off does not, and a row that stands in several versions counts once. The count
  is read from the rules, so the pull request that builds the rules brings it. It rules out a
  count of records, which grows with every merge. Built: the page reads the store once and
  counts, for each profile, the nodes of the tree at which a standing version of its row
  grants, `StoredRules.Named`, so a row whose standing versions all take the rule off and a
  row that is only lost are left out, and `DatabaseNodes` walks neither the recycle bin nor the
  plugin's store. *Held by* `DatabaseProfilesTests` and `DatabaseNodesTests`.
- **It shows entries that are passed over**: an entry with `KPG.` strings that is not the
  store's (§621). It lists no copy as taking anything away.
- **It states a database that keeps little or no history** (decision 20), since in such a
  database a single edit of a record's content stops the database or loses the store key (§622).
- **It shows the store's size** (decision 21), since the store grows without pruning (§643).
- **It reports a missing authority**, which is how a lost store key appears (§632).
- **It holds "Seal again"** for a stopped store, with the page of §625.
- **It holds the command that starts the store again**, with its warning (§632).
- **It asks which store to keep** where several store keys stand (§649), as it asks about
  several authorities today, and it shows what passes under a key that was set aside.
- **It states the store's format and the version that wrote it beside the plugin's own**, where
  the plugin is held back or the store waits to be raised, and it holds the button that raises
  a store (§652).
- **It is the only place that takes anything away.** Deleting an entry of the store by hand
  revokes nothing, which the notes of every record state (§607).
- **It offers no command on the store where nothing is written into it in the session**, as
  where the dialogs that would take a write back are not watched (§627), and states that and
  why, as under a word. The clients page then shows no X, and its rows' menus neither
  **Revoke** nor **Take certificate away**; **Allow renewal** stays on a client's own row, since
  it lifts an answer for this session, and a stored answer it cannot lift is refused there with
  the reason, under the list as "Renewal stays refused:" and the reason (§618). The Profiles
  page then shows neither the row that adds a name nor an X (§616), and shows neither under a
  store that cannot be judged either, where the clients page lists nobody. Built, and held by `AuthorityPanelTests`, `OpenDatabaseTests`,
  `ClientListTests.Where_the_store_takes_no_write_no_row_carries_the_X`,
  `ClientListMenuTests.Where_the_store_takes_no_write_the_menu_offers_Allow_renewal_alone` and
  `ProfileListTests.A_page_over_a_store_that_takes_no_write_offers_no_Add_and_no_X`.

Nothing of the tab was run.

### The grant editor §657

**A rule that stands in several versions shows as that many rows, bound together** (a later
decision, taken from pictures of a prototype in KeePass's own group and entry dialogs). What
the operator sees is decided. How it is drawn was proposed with it, and "What is built of the
drawing" below has the drawing as built, with where it departs from the proposal.

**What the operator sees.**

- **A row for each version**, with the profile's name on every row.
- **A bracket in front of the rows that binds them**: closed at the first and at the last row of
  the group, and open where the table's scrolling cuts the group.
- **A light red background** behind the rows of the group.
- **In bold, the letters of the mask in which the versions do not all agree.**
- **An X on each row, as on every row, which removes that version**: the version of that
  profile's rule and nothing else.
- **The rows of a group in the order of their masks**, which is the same in every copy. No time
  orders them.
- **A tooltip on every row of the group**, which states how many versions there are and what the
  gate grants until a single version is left: what all of them allow.
- **More than two rows where more than two versions stand**, since several gates are supported.

**No message box, and no dialog held at OK.** The group dialog and the entry dialog close at OK
with the versions left as they are, and the gate goes on granting what all versions allow until
the operator removes versions. Nothing reaches ahead of the group dialog's OK, and the entry
dialog's event that precedes a save is not used for this. The message that §629 has for an OK
whose write was refused is no such box: it is shown after the dialog has closed, for rules that
were not written, and never for versions that stand.

**A row of a group offers its X and no list of rules** (a later decision). The operator removes
versions until a single row is left, and changes that row then as any row is changed, on the
same tab and before OK. So no single press on a group grants more than some version of it
held. **Considered and not taken:** a choice of a rule on any row of the group that sets the
profile's rule in a single version replacing all of them, which settles the rows with a single
press and can grant more than the versions allowed together.

**A switch in versions that differ shows a third mark, which counts as ticked** (a later
decision). Where the store holds a version of the node's switch that refuses beside a version
that lets the walk go on, the check box "Refuse any profile not named here" shows the mixed
mark, neither ticked nor empty, with a tooltip. The tooltip states, in words that are proposed,
that copies of the database set the switch differently and that the gate refuses any profile
not named until it is set again. The state counts as ticked: the gate refuses any profile the
node does not name, as §617 has it for versions of the switch that differ so, and an X pressed
under it with the box left alone writes `off:deny` (§617). **The operator can only tick or
clear the box.** The first press ticks it, the next clears it, and no press brings the mixed
mark back. OK writes a version of the switch only where the operator pressed, naming the
versions the tab showed (§629), so an OK with the box left alone leaves the versions as they
are and the gate refusing. The mixed mark is shown only where the store holds versions that
differ so. Two versions that both let the walk go on and hold other letters show the box
cleared, and each profile they cut shows its limit (below); ticking the box and clearing it
again writes a single version that settles every profile there (this document's own, from the
reading of §617; in no run). **Considered and not taken:** a box shown as ticked there, which
reads as settled when it is not, and under which settling on what the box already shows takes
a press and a second press that look like nothing.

**A profile that a node cuts shows two rows, bound together, and a single press settles it** (a
later decision, with the record of what arrived, §617; in no run, and the drawing is proposed).
A node cuts a profile where its switch lets the walk go on, its row lets the profile through,
and more arrives than every version that lets through recorded. The tab then shows, bound as
the rows of a rule in several versions are:

- **the profile's inherited row**, in gray, with the mask that arrives, as an inherited row is
  drawn;
- **under it a single limit row**, with the letters that all those records hold, the letters
  that differ from what arrives in bold, and an X. There is a single limit row for a profile,
  however many versions cut and whether they are the row's or the switch's, and for `off:deny`
  it shows the mask with no letter.

The tooltip states that the node lets through what arrives for the profile only as far as it
had arrived when that was set, with that mask, and what the gate grants there until the limit
row is removed, which is what both rows allow. **The X on the limit row is the press that
settles** (§617): at the dialog's OK it writes anew the versions at the node that let the
profile through with what arrives, and from the press on the inherited row stands alone on the
tab. It writes nothing before OK, and it is the whole of what the operator does: no rule has to
be set and taken off again. The limit row offers its X and no list of rules, as a row of a
group does, and on a tab that takes no change it shows with no X. **Nothing shows where nothing
is cut**, also for a version that cut before and no longer does, and nothing marks a node whose
versions recorded exactly what arrives (§639). While the switch refuses on the tab, no limit
row shows; at the press that clears the box every inherited row goes live, and no limit row
shows for them, since the OK writes those versions anew (§617). Where a row stands in granting
versions beside a version that takes the rule off, the group shows that version with the
letters it holds, and its X removes that version as the X of any row of a group does, which is
not the press that settles. *Held by* `GrantSheetTests` and `GrantPanelTests`, and in a real
dialog by `GrantEditorInRealDialogsTests`.

**How it is drawn** (proposed, from the prototype). The bracket is two pixels wide, in the
cell's text color, in a left padding of the profile's cell. The red is the window's color moved
a sixth of the way toward red. The letters that differ are bold in the system's highlight
color. What the prototype left weak is part of S9: a bold dot is faint, the bold letters have
less contrast on the red, the table shows four rows, so a further version pushes the line for
adding a profile out of sight, and a high contrast setting was not looked at.

**A row is a record, and only the same profile changed apart shows as several rows.** A rule
is stored for each node and profile (§617), so the rows of a group are the standing versions of
a single profile's rule at that node. A profile that two copies did not both change shows a
single row, whatever else the copies changed at the node. **Nothing else at the node is held
back**: while a group of rows stands, the other profiles' rows and the switch are changed as
ever, and the dialog's OK writes a version for each row that was changed and leaves the group
as it is.

**Removing a row of a group writes again each version that is left** (§617, fixed as
measured). The removed version then stands in no copy the write reaches, and the rows left
stay as they are. Where two copies each remove a different row of the same group apart, every
row is back once the copies meet, and the operator removes rows again.

**Every row has an X, and the X takes the rule off** (a later decision, §617, which reverses
the paragraph after this). Under a switch that refuses the X writes `off:deny` and the row
goes from the table, as it goes under a switch that lets the walk go on. Where the switch is
later cleared in a copy that never held that removal, the profile shows its inherited row with
a limit row of no letter under it, and the row that was removed does not come back as a row of
the node's own. So the table holds no row that cannot be taken off, and how to show such a
row, which S9 held open, is settled by that. An empty rule is a rule the operator chose,
or that sealing the store again wrote.

**Reversed: a row removed under a switch that refuses stays in the table, as an empty rule** (a
later decision, §617). Where the node's switch refuses any profile not named, the X on a profile's
row writes a version that grants nothing, so the row stays, shows the mask with no letter, and
is there until the operator removes it again. Where the switch lets the walk go on, the X takes
the rule off and the row goes. The X on a row that is an empty rule already writes nothing
while the switch still refuses, as run (§617), so that row goes by a removal made once the
switch lets the walk go on. How the table shows the operator that such a row cannot be taken
off yet is not fixed, and is part of S9.

**Where the tab takes no change** (this document's own). The tab is read only, and states why,
wherever a change could only be refused at OK and that can be known as the dialog opens:

- where no store key stands, as in a database that has had no certificate authority yet (§627
  step 1);
- where the store holds the gate back or cannot be judged (§656, §649);
- where nothing is written into the store in the session (§627);
- on a node of the plugin's store, which no route reaches (§633) and whose entry dialog puts
  its copy of the entry back at its OK (§626), ahead of the guard of §635;
- where several entries are edited together;
- on an earlier version of an entry, where the tab shows the entry's rules as they stand, since
  a rule belongs to the entry and to no version of it (§617): every row the entry's own tab
  shows, a rule from above, a limit and a rule of a name gone among them, found through the entry
  of the revision's identifier, since KeePass reads a revision from the file under no group;
- and where the dialog names no node or no database.

The rows are shown wherever the store is whole or stopped. It rules out a press that is refused
only at OK. **The reason stands in place of the line that states where rules come from**, which
tells that a rule can be overwritten and so stands only over a table that takes a press. *Held
by* `GrantSheetTests` and `GrantEditorInRealDialogsTests`.

**A database that names no profile is told over the table**, before any press, with where a
profile is named, the Profiles page of the KPG tab in the database settings. That is no reason
of the read only kind, since the box and the X on the row of a name the list no longer holds
still take a press that OK writes; the table offers no empty line then, since no profile can be
chosen on the line. **The notice stands in place of the line that states where rules come
from**, as a reason does, since that line tells that a rule chosen on the table overwrites what
arrives and no profile can be chosen there: the notice is all that stands over the table, and
the table takes the room of the line. A record that names a profile brings the
line back with the empty line, as the tab reads its node again. *Held by* `GrantPanelTests`, and
in the room of the real group and entry dialogs at each scaling by `GrantTabFitsTests`.

**A row or a switch that refuses is shown, and setting it replaces it** (this document's own;
not run). A row that refuses for a lost version or for a text out of form (§617) shows as a
rule that grants nothing, with a tooltip stating that a version of the rule was deleted and its
content is nowhere, or that a version of it does not read, so that the profile is refused at
the node. Choosing a rule for that row replaces it: the version written names, beside the
versions the tab showed, each lost version by the identifier of the deletion record that tells
of it, and from then on that deletion record tells of a replaced version (§617). Where such a
row stands in several versions besides, it offers its X alone as any row of a group does, and
is given a rule once a single row is left. **No press is offered that OK could not write**
(this document's own, with the build): removing a version writes again each version left, so
the X removes a version only where a version that reads is left to write again. The last
version that reads beside a lost version therefore offers no X, and in a group no version of
which reads, every row takes a rule, which replaces every version, and its X takes the rule
off, every version with the rule. A switch that refuses in that way shows as refusing,
and setting it replaces it in the same way. Without that, a row that refuses in a store that is
whole could be lifted only by sealing the store again (§625), which is a press for a stopped
store. *Held by* `RuleActsTests` and `GrantSheetTests`; a row that refuses, replaced in the
editor and merged back over synchronized copies, is left to a later commit (§644 item 24).

**A state has a column of its own, and each of its words a tooltip** (a later decision). The
table holds the profile, its permissions as a mask, a State column and the X. A State cell
holds a word and never a name: where several states hold for a row, the first of this order,
and the tooltip carries the sentence of each. The words are:

- "No profile": the database no longer names the profile of the rule.
- "Missing": a version of the rule was lost, its record deleted by something other than the
  plugin with its content nowhere, and the gate grants nothing at the node until a rule is chosen.
- "Unreadable": a version of the rule stands in a text that cannot be read, with the same effect.
- "Unmerged": copies of the database set the rule differently before they were synchronized, so
  the rule stands in several versions, a row for each; until a single version is left, the gate
  grants what all of them share.
- "Removed": among those rows, a version in which the rule was taken off.
- "At most": the limit row under a profile that the node cuts. The rule here was taken off, or
  the box cleared, when less arrived than arrives now, and the node lets through only what
  arrived then, which the row shows.
- "Refused": the box "Refuse any profile not named here" is ticked, so a rule from above stops at
  the node.
- "Inherited": no rule at the node, and the rule of a group above applies.

A rule set at the node in a single version, in force as shown, has no word. **Missing and
Unreadable are two states** of the sheet, and the panel draws the word of the state, so the two
are never told apart by comparing a text: `GrantState.Missing` for a version the store holds as
lost (§617) and `GrantState.Unreadable` for a version whose text is out of form. **The tooltip
of the cell explains the word** to an operator meeting the word for the first time: what
happened, what the gate does for the profile at the node in that state, and what a press on the
row does; a sentence that names a state names the state by its word. The same text is the cell's
accessible description and its accessible help, which UI automation reads as its help text;
nothing of it is printed under the table. A sentence of the tab names no profile and no client,
which a cell of the table may, and names its subject and its object where a pronoun would
stand. **A row or a box that a press changed is told by a further line of its tooltip and by
its accessible text**, and never by a color alone; each line of a tooltip is folded as any
tooltip of the plugin is. **A rule that the X removed shows, until the dialog closes, as the
profile's inherited row in its place**, marked as changed, with the mask that arrives and no
letter where nothing arrives, so that every change that waits for OK has a row; where the box
refuses, that row is Refused, struck through, as any row the node does not name.

**Enter and Escape are the dialog's OK and Cancel** wherever the table holds the keyboard and
no picker is open, as on every other page of the dialog, and a picker's own while the picker
is open. **The columns keep their widths for the life of the dialog**: the profile's column
leaves the room of the vertical scroll bar whether or not the bar shows, the Permissions column
is as wide as a mask whether or not a row shows a mask, the State column as its longest word,
and the last column as its bin or its X. **The panel has a name of its own** for UI automation,
and the box's accessible object answers the box's whole tooltip as its help; UI automation's
own proxy for a Windows button reads the tooltip's window instead, cut after 127 characters,
measured. **Where an OK kept some changes and left others out**, the message after the close
ends with a line telling that the KPG tab of the group or entry shows the rules as kept, since
no sentence names a profile.

**Every text that stands above the table takes at most two lines** (a later decision): the
line that states where rules come from, each reason for which the tab takes no change, and
each notice. The table is what the tab is for, and in the group dialog every line above it is
a row less. Measured in the real group and entry dialogs at each scaling KeePass is run at
(`GrantTabFitsTests.Every_text_above_the_table_takes_at_most_two_lines`): with the longest
text standing, the group dialog's table shows its heading and two rows at a scaling of 1.00
and four at 1.50, and with no text standing three and six; the entry dialog shows eleven rows
and more.

**What is built.** The editor as a buffer over the store, with every row and state of this
section. `GrantSheet` decides what each row
stands for and what each press hands to OK, with no window in it: a rule of the node's own, a
rule arriving from above, in force or refused by the box, the limit row under a profile that
the node cuts, a row for each standing version of a rule in several versions in the order of
their masks, a lost row, missing or unreadable, and a rule for a name the database no longer
names. It takes a rule
chosen on a row of the node's own, on an inherited row and on a lost row that stands alone,
and none on a row of a group but where no version of the group reads; every row carries the X
but an inherited row and the last version that reads beside a lost version, whose removal no
write could make; the X on a limit
row is the press that settles; and the check box shows cleared, ticked, the mixed mark, or
ticked with the tooltip of a lost setting. `GrantPanel` draws the sheet: the masks in a
monospaced column, a rule from above in gray, a rule the box refuses with a line through its
mask, the State column with its tooltips, and a text above the table for a tab that takes no
change, with its reason, each of the cases above and a KeePass that does not tell the
database, the group or the entry of a dialog among them. The tab reads its node's rules, what
arrives and the names from a single reading as it opens and again where a record lands, a row
the operator pressed keeping its press. `GrantMarks` draws the bracket, the colors and the
letters in bold, below. **Left to a later commit**: the pictures in a real KeePass, and the drive
there of the rows and states no fixture of the tree drives, named at the end of this paragraph
(§644 item 23). *Held by* `GrantSheetTests`, `GrantPanelTests`,
`GrantPickerTests` and `GrantTableFitsTests`; and with the real `GroupForm` and `PwEntryForm`
by `GrantEditorInRealDialogsTests`, each case in which the tab takes no change, the tab over a
node's rules and what arrives, and the dialog that adds an entry among them, by
`GrantTabFitsTests` at each scaling, and by `RightToLeftTranslationTests`. In a real KeePass
the editor was run on the store for rules set on the tabs of the group, entry and Add Entry
dialogs and written at OK, `LiveGrantEditorTests`; for the rows of a rule in several versions,
out of view and scrolled into view, and of a limit, each line read through UI automation, with
what an X and OK write, `LiveRuleSeededStatesTests`; and for a picker left open under a dialog
that closes, `LiveGrantPickerCloseTests`. No lost rule, no rule of a name the database no longer
names, no rule the box refuses, no box in the mixed mark and no tab that takes no change was
driven there by a fixture of the tree.

**What is built of the drawing.** The rows of a group, the versions of a rule and a limit under
its inherited rule, stand in a bracket in a padding before the name in the profile's cell, the
width of the ordinary spacing, drawn as a line as thick as the tight spacing in the table's text
color, both grown with the text: closed over the first row and under the last, and open where
the table's scrolling cuts the group. Behind the rows of a group, a group in which no version
reads among them, and behind the box in the mixed mark, on the box's own line, a light red
(#FFEBEB). In bold, in the row's own text color, each place of the mask in which the masks that
read in a group do not all hold the same character, on each of them, a version that takes the
rule off among them: a lost or unreadable row and a mask struck through take no bold, and a
group with a single mask that reads has none. Every mask is drawn a letter at a time in the
places the cell draws the mask itself, left to right also in a table that reads right to left,
so a bold letter is the only difference between two masks, and a mask of every letter in bold
ends inside its cell. Behind a row or a box that a press changed and that OK writes, a light
yellow, #FFF3B0, over the red, beside the line of its tooltip and its accessible text: a rule
set, a rule removed and pending, a limit settled, a version removed, a version taken off shown
with the letters OK records, a pending change with no letter to show, and each row whose state
or letters a press on the box changes, a rule from above refused or arriving again and a limit
coming, while a row that goes needs no mark; a first showing marks no row. **A change that no
OK can write takes no yellow**, where a reading made again found the database no longer taking
rules, since the yellow tells that OK writes the change; its tooltip and its accessible text
tell that OK writes nothing, and a row of a group keeps its red. **A selected cell takes the
selection's colors on every row**, an inherited rule's and a colored row's among them, and the
rest of the row keeps its gray or its color; the bracket is drawn in the selection's text color
in a selected cell and in the table's on every other, the same on every kind of row. The gray of
an inherited rule and the line through a refused mask are drawn beside these marks, which take
no row height and no column width. The strip between the table and the box lends the table up
to its height where the rows lack that much, so a last row cut by its own bottom line stands
whole with no scroll bar beside it; rows that lack more scroll as ever. A fault in drawing the
marks is recorded once, and the table is then drawn by the grid alone, words and tooltips
whole, until it is filled again. On the
light yellow the window's text has a contrast of 18.7 to 1 and the gray of an inherited rule 4.6
to 1; on the light red, 18.3 and 4.5 to 1. The red is lighter than the prototype's so that the
gray keeps that contrast, and the bold letters keep the text's color, where the highlight color
had less contrast on the red. A bold dot stays faint, and the letter in the same place on
another row of the group is what the eye finds. **Under a high contrast setting no row and no
box takes a fixed color**, and a change of the display's colors reaches every row and the box
at once, on a tab that takes no press as on any other: the words, the tooltips, the accessible
texts, the bracket and the bold letters tell every state, as they do with every color alike.
The light yellow is defined once, as `RowTints.ChangedColor`, for every page of the plugin that
marks a change waiting for OK. *Held by* `GrantMarksTests`, `GrantPanelTests`,
`GrantDrawingTests`, `GrantSheetTests`, `GrantTableFitsTests` and, in the room of KeePass's
group and entry dialogs at every scaling, `GrantTabFitsTests`.

---

## 10. Other programs and operations §637

Which programs a KeePassGate database can be shared with is published later, as a list of its
own in the operator's documentation, from the measurements (decision 5). It names operations
beside programs. This section is what that list rests on: evaluation 5 holds each program
against the three criteria of §602 with every run, and the reports it cites hold the runs.

| Program | Standing | Conditions |
| --- | --- | --- |
| KeePass 2.62 with the plugin | Compatible, the reference | Synchronize is the only way two copies are brought together. Answering "Overwrite" when KeePass reports a changed file loses the other writer's changes, and Save As over the same path writes without asking. A database that keeps little or no history is named as a condition |
| KeePassXC 2.8.0 | Compatible, with conditions | Its merge carries no deletion record, so what was deleted comes back. "Overwrite", or "Ignore" and then a save, loses the other writer's changes. Its window was measured for 2.8.0 only; four releases were measured through `keepassxc-cli` |
| Keepass2Android 1.15-r6 | Compatible, with conditions | "No, overwrite" is the only losing sequence found. "Always merge on conflict" removes the question and is a setting of the installation. Changes saved while the server is unreachable wait in the phone's cache until a synchronize is run. Every run was on an emulator |
| KeePassDX 4.5.8 | Incompatible, by criteria 2 and 3 | It writes over a changed file at its next save with no question that could stop it, and its merge keeps no deletion record. Saving alone in it keeps every place the store uses |
| KeePassDroid 2.7 | Incompatible, by criteria 2 and 3 | It writes over a changed file without asking and has no merge. Measured on an emulator |

**The third criterion is applied for each place the store relies on.** KeePassXC applies and
carries no deletion record, and is listed with that as a stated condition: what the plugin
replaced comes back and stays out of force, since the record that replaced it names it
(evaluation 5.3).

**Synchronize, and not Import, brings a copy up to date.** KeePass's File, Import of a copy of
the same database preselects Create new IDs, 5 of 5 runs (`measured-desktop-windows` 5.2). Every
entry of the other copy then arrives a second time under a new identifier. The store reads none
of the copies of its records, so such an import leaves the store as it was (§640).

**Grant on groups, and not at the root, where imported or pasted copies must stay unserved.** The
same import adds every top-level group of the operator's own a second time, with what it holds,
as a new group at the root beside the original. The plugin cannot tell a copy of an ordinary
entry from a new entry, and such a copy is served as the root's rule gives (§639).

**The operations the list names** (evaluation 5.7):

- **Supported, to bring two copies together**: KeePass's Synchronize, as a command or as the
  answer to its question at a save; KeePassXC's Merge; Keepass2Android's "Yes, merge", its "Always
  merge on conflict" and its "Synchronize database...".
- **Named as losing**: every answer that overwrites, which is KeePass's Overwrite, KeePassXC's
  Overwrite and its Ignore followed by a save, and Keepass2Android's "No, overwrite"; and
  KeePass's Save As over the open file's path.
- **Named as not bringing a copy up to date**: KeePass's File, Import of a copy of the same
  database with its preselected method.
- **Named as unsupported on a KeePassGate database**: Delete Duplicate Entries after such an
  import or after a duplicate of the plugin's entries; Database Maintenance, both its
  deleted-objects page and its history page; XML Replace; deleting, editing, moving or copying
  the plugin's entries by hand; emptying the recycle bin while it holds an entry of the
  plugin's; and naming the plugin's group, or a group inside it, as the recycle bin, which moves
  what a client removes into the plugin's group, where every client finds it absent (§633).
- **Named as a condition**: a database whose settings keep little or no history.

**A file synchronizer** between two writers was not measured. Whatever replaces the file with an
older copy has the effect of the first sequence of §639.

---

## 11. Attacking it §638

No store inside the file survives everything, and this store does not. What follows is every
sequence of evaluation 6 that remains under the design as decided, by what it ends in. Each names
its row there, since the evaluation and `measured-decided-store` cite the rows by number.
`measured-store-as-a-whole` numbers the rows in its section 2.4 as an earlier draft did, so this
document cites that report by its sections and never by a row number of its own. A sequence
accepted by a decision is neither measured nor closed by being accepted. **A paragraph that names
no row is a sequence the evaluation does not hold**: it comes with a later decision or with a
part this document proposes, and none of those was run.

**How far the design was run as a whole.** In the model: twelve configurations over two and three
files, 20,000 random histories each. Without a deletion by hand of the authority's entry, 3
histories in 24 cells failed, and with Synchronize and Keepass2Android as the only merges none
failed (`measured-store-as-a-whole` 2.1). On real files, as decided: 234 merge cells over
KeePassLib's five methods and four KeePassXC releases, of which 221 ended as decided, 3 narrower,
and 10 are the import of §640; 144 merges after a deletion by hand, none wider; 210 merges after
every deletion record was removed, all as decided; 590 cells of an edited, moved or deleted entry
of the store, none wider; and 300 random histories with no failure of the model's check
(`measured-decided-store` 2, 3). Every part ran twice with the same output. Nothing was run with
the API, as the plugin, with the guard, in a window or on a device (§644). A sequence that needs
what was not run is in no paragraph below.

### Fail-open §639

Each of these ends with a client reaching more than the last decision allowed.

**An older copy is written whole over the newer file** (row 1). *Who:* a person who answers
Overwrite in KeePass, Overwrite or Ignore in KeePassXC, or "No, overwrite" in Keepass2Android; who
saves with KeePass's Save As over the same path, which asks nothing about the changed file; who
uses KeePassDX or KeePassDroid; or a file synchronizer that puts an older copy back. *What
happens:* every entry written since that program's load is gone, the summary entry with it, and
nothing in the file shows it. A revocation made in between is undone. *What covers it:* nothing in
the store. Nothing about a database is kept outside its file, and an older file written over a
newer file on purpose is accepted (decision 2). The conditions of §637 name each losing answer.
*Measured:* KeePass 10 runs and 1 for Save As, KeePassXC 5, Keepass2Android 21, KeePassDX 22,
KeePassDroid 3, the phone programs on an emulator (`measured-desktop-windows` 9;
`measured-overwrite`; `measured-k2a-synchronize`; `measured-keepassdx` 4.1). A file synchronizer
was not measured.

**A decision is deleted in both of its entries, and a merge then carries no deletion record**
(row 2). *Who:* a person who deletes two entries of the store for good, a decision's own record
and the summary entry, in the copy where the decision was made, past the guard or with it off;
then KeePassXC, or an import method of KeePass that carries no deletion record. *What happens:*
the other copy is never told, and admits the revoked client or keeps the removed name. *What
covers it:* a Synchronize, which carries the deletion records and stops the database.
*Measured:* in the model, replayed, and 3 histories in 24 cells of 20,000
(`measured-store-as-a-whole` 2.1, 2.4); and through the product for a certificate taken away,
over real databases merged by `PwDatabase.MergeIn`: with the record and the summary entry both
deleted for good where the certificate was given up, that copy is stopped, Synchronize stops
the other copy as well, and Overwrite if newer, Overwrite existing and Keep existing leave the
other copy whole and admitting the certificate, the renewal that arrives and its own renewal
(`StoreAdmissionMergeTests.A_record_taking_a_certificate_away_deleted_for_good_with_its_summary_entry_reaches_only_what_Synchronize_carries`).
No statement about the certificate is left in the file merged into, so nothing of §648 is asked
there.

**A decision is lost a second time, without a trace, in a copy that received it through a merge
that carries no deletion record** (row 3). *Who:* the decision's record is deleted where it was
made, a KeePassXC merge delivers the summary entry alone, and that entry is then deleted with no
deletion record, as a script over pykeepass deletes. *What happens:* that copy is its older self:
it admits the revoked client, or gives a narrowed node what it gave before. *What covers it:* a
Synchronize with a copy that still holds the decision ended as decided. *Measured:* in the model,
and on real files, where the copy that removed a narrowed rule's new version and summary entry by
script ended wider (`measured-store-as-a-whole` 2.4; `measured-decided-store` 5).

**Every deletion record is removed while a loss of both carriers stands, and a stale copy is then
synchronized** (row 4). *Who:* KeePass's Database Maintenance, which asks nothing. *What happens:*
the older rule is back. Sealing the store again before the stale copy arrives does not help: the
node inherits after the press and gives what the older rule gave after the merge. *What covers
it:* nothing in the store. The mark that the design paper put on a ruled node held this replay,
and the mark is left out (§612). Database Maintenance is named as unsupported (§637).
*Measured:* on real files and in the model (`measured-store-as-a-whole` 2.4, 3.7;
`measured-decided-store` 5). With a record for each node and profile
(`measured-rules-cut-parts` 3), the hand deletion of those runs takes a single row's newest
version: where the other row's new version is left with no summary entry to carry it, the
store stops, and the sequence refuses as a whole where the whole-rule form ended wider; with
the store sealed again before the stale copy arrives, the row deleted is wider and the other
row is as decided. The loss of every row of a node at once was not run. **A replaced version
was a third carrier by accident** (reasoning, §617): while replaced versions stayed in the
file, a standing version lost in both carriers left an earlier version of its row standing,
and once a write deletes what is replaced (§627), such a row, with every deletion record gone,
reads as a rule never made.

**The store is sealed again after a decision was lost in both carriers with no deletion record**
(row 5). *Who:* a script, or a merge that keeps no record, removes a revocation and the summary
entry; the store stops; the operator confirms. *What happens:* the revoked client is admitted in a
stale copy after a KeePassXC merge, a node whose first rule granted nothing inherits, and a
refused renewal is asked again. With deletion records present the press writes the refusal down
and the result is as decided. *What covers it:* the page states that it cannot know what was
deleted without a record (§625). A deletion record that names no entry, the design paper's
witness, held the revocation's case and is left out (decision 15): a deletion by hand followed by
the confirmation is the person's own act, the witness did not travel through a KeePassXC merge
from another database, and Database Maintenance removes it with every other deletion record
(`measured-desktop-windows` 3.3, 5.2). *Measured:* in the model (`measured-store-as-a-whole` 2.3,
2.4), and for an answer to a renewal in the model and on real files
(`measured-renewal-answers` 5, 7). *Pinned* over real databases by `StoreSealMergeTests`: the seal
is shown no revocation and restates none, and none is in force after any of KeePass's own merges
with a copy from before the revocation.

**The store is sealed again after a decision was deleted in both carriers, each with a deletion
record, whose subject nothing left names** (no row of its own; closed, §611). *Who:* a revocation, a
retirement or a removal of a name is made in a copy, and its record and the summary entry are
deleted with a deletion record each, past the guard or with it off, so that the client's record, the
authority's or the addition goes with the decision and nothing left names the subject; the store
stops; the operator seals it. *What happens, where the seal restates the decision by its subject
alone:* the seal's summary entry restates nothing of it, the deletion record is its only carrier,
and a copy from before the decision then brings the subject back: by Overwrite if newer, Overwrite
existing, Keep existing or KeePassXC into the copy from before, or by any method but Create new IDs
once Database Maintenance has removed the sealed copy's deletion records. The revoked client is
admitted, or the removed name back on the list; the retired authority stands beside the new
authority, and nothing is served until it is kept. *What covers it:* the seal's item with no
subject, carried by every write until the subject is named, which refuses the subject when it comes
back (§611). *Measured:* in the model and on real files: restated by its subject alone, 46 of 972
cells on files ended wider than their own ideal and 23 granted less, and the form built never
differs from the model's control in any part (`measured-seal-restates` 4 to 6). *Pinned* by
`StoreWriteShapeTests`, R5 to R7 and F5 to F7r of what the seal restates, and over real databases by
`StoreSealMergeTests`, F5 through each of KeePass's merges in both directions, with the deletion
records kept and cleared.

**A lost answer whose client key the file no longer knows** (no row of its own; §618). *Who:* a
person who deletes a client's admission by hand in a copy whose store holds the deletion record of
a lost answer of that client, and no other record or item names the client key. *What happens:*
the deletion record tells of nothing, so the answer it refused is refused no more; the client is
admitted nowhere in that copy. *Measured:* in the model, in the random histories of Seal again,
where no seal was involved (`measured-seal` 6).

**A file is merged in that was made to grant, by somebody who holds the store key** (row 7).
*Who:* the store key is a string in an entry of the database and is never renewed. So whoever can
open the database, or could open any copy of it made since the first authority, holds it, also
after the master key was changed (reasoning). That person writes, by a script and without the
plugin, a rule version that passes the check and names the versions it replaces, with a summary
entry, and that file and the gate's are then brought together by Synchronize or by an import
method that matches identifiers. A client of the API cannot do this, short of a defect in the API
(row 13). A file from outside the database, made by somebody who never held the key, cannot
either, and what holds that is different for its two forms. Its records under no key, or under
a key the file does not bring, are passed over, which was measured in the model with planted
entries. Its records under a key that the file brings with it are under a second store key and
stop the gate (§649), which was not run; under the measured reading that file ends wider, as
§649 sets out. *What happens:* the grant counts. A node narrowed at the gate gave read and write
at the gate and in both files after KeePass's Synchronize. The same person can equally bring a
store under a key of their own, with a keep record that names the database's key: that store
then stands alone and is served, with no dialog (§649, not run). *What covers it:* nothing. The
key is carried over a Recreate, and no repair runs. *Measured:* on real files, for the holder of the
key (`measured-decided-store` 2.2).

**The operator keeps the wrong store.** *Who:* a file from outside that brings a store key of
its own, with an authority, admissions and rules under it, merged in by the operator; then the
operator, asked which store to keep, keeps the outsider's. *What happens:* the database's own
key is set aside, everything under it counts for nothing, and the gate serves the outsider's
authority to the outsider's clients under the outsider's rules. *What covers it:* what the
dialog shows of each store (§649), and a later answer, which takes the wrong answer back. It
takes a wrong answer, as an overwrite does. *Measured:* not run.

**A gate answers from a copy that predates a revocation made at another gate** (row 8). *Who:*
two gates, the second of which has not synchronized since. *What happens:* the second gate admits
the client until its KeePass takes the file in. A renewal it approves meanwhile is refused once
the copies meet. A certificate taken away at another gate is admitted in the same way until
then. A renewal made from it at the second gate in the meantime does not count once the copies
meet, since the record that took the certificate away does not name it. A renewal made from it
before it was taken away stays admitted: only the operator stops a certificate that somebody
took and renewed, by taking that certificate away as well or by revoking the client (§648).
*What covers it:* a Synchronize at every gate. *Measured:* in the model, and on real files for
the renewal (`measured-store-as-a-whole` 2.4, 3.1); and for a certificate taken away through
the product, two copies of a real database written apart and merged each into the other by
`PwDatabase.MergeIn` under each method (`StoreAdmissionMergeTests`, §648). Two KeePass
instances with the plugin were not run.

**A node with no rule of its own comes to sit, or stays, under a grant** (row 9). *Who:* nobody
has to make a mistake. KeePass's Synchronize takes the later of two moves, and a KeePassXC merge
of an older copy brings a deleted node back. *What happens:* the profile reads the node. *What
covers it:* a rule on the node itself, which held in every cell of 90
(`measured-store-as-a-whole` 3.2). Access may rest on a node's place (decision 9). *Measured:* on
other options' records and on generic carriers; not replayed on the store. **Run on the store
since, for a move made apart from a change above** (`measured-rules-across-levels` 5, 8 and
13, H7 there): a copy moves a node that has no rule of its own under another group while
another copy, apart, widens the rule of that group or of the group it came from, and after a
synchronize the node sits under a grant that neither copy's table gave it there, on files and
in 79 of 200,000 histories of the model with the move in its alphabet. The record of what
arrived does not touch it (§617): such a node holds no version that could cut. It stays
accepted, and what has to stay closed wherever it is moved needs a rule on the node itself.
*Held by* `StoreRuleMergeTests`, pinned at what the sequence on files gave, beyond both copies,
the merge either way.

**A copy of a narrowed node is read under the rule of the place it lands in** (row 10). *Who:*
anybody who duplicates, copies or pastes a node, and an import with Create new IDs. *What
happens:* beside its original under a group that grants, the copy gives what the group gives. The
copies that an import with new identifiers makes of the operator's entries and groups land in new
groups under the root and give what the root's rule gives. The plugin cannot tell such a copy
from a new entry: an import raises no event that a plugin can see, and nothing in the file marks
an entry as a copy (reasoning). *What covers it:* nothing in the store, since the plugin writes
nothing into the operator's entries and places no record in the operator's groups (§612). The
operator's documentation advises to grant on groups and not at the root where imported or pasted
copies must stay unserved, and to narrow on the group, and not on a single entry, for what has to
stay closed beside a duplicate. *Measured:* on real files and in the model: 22 readings of a
copied node on files, and up to 17 histories of 20,000 a cell (`measured-store-as-a-whole` 3.2;
`measured-decided-store` 2.1).

**An entry of the store is deleted by hand to take a client or a name away** (row 11). *Who:* a
person who deletes an admission as any entry is deleted, past the guard or with it off. *What
happens:* in the recycle bin the entry still counts. Deleted for good, the client is refused only
where the deletion record reaches: a KeePassXC merge leaves the other copy admitting the client,
and a merge with a stale copy brings the entry back where no deletion record remains. That is
wider against what the person meant, and not against a decision the plugin was given. *What
covers it:* the KPG tab is the only place that takes anything away, and a revocation made there
holds. The notes of every record state it. *Measured:* on real files
(`measured-store-as-a-whole` 3.4); and through the product for an admission that a renewal was
made from: deleted for good by hand, the renewal is refused in that copy, its certificate of
origin being nowhere (§648 rule 5), and in the other copy once a Synchronize has carried the
deletion record, while Overwrite if newer, Overwrite existing and Keep existing leave the other
copy admitting the certificate of origin and the renewal by it
(`StoreAdmissionMergeTests.An_admission_whose_admission_of_origin_was_deleted_by_hand_counts_only_where_its_origin_does`).

**A route of the plugin's own API serves or accepts something of the store** (row 13). *Who:* a
defect in the product on a single route, and a client holding ordinary rights there. *What
happens:* the client reads the authority's private key and the store key and makes a record that
passes, or plants or removes a refusal (reasoning). *What covers it:* the test of every route,
which is the condition of §633. *Measured:* `StoreOnEveryRouteTests` runs every route declared
against a composed store, in process and not over TLS; a route added later is covered once it has
its case, which the test refuses to run without.

**A former recycle bin becomes an ordinary group** (row 14). *Who:* KeePass's Synchronize deciding
the bin by a time, or a KeePassXC merge bringing the source's bin in as a group. *What happens:*
clients reach what was deleted. *What covers it:* in 0.3.0 nothing in the store, which keeps no
record of the bin (§619). The cover is today's: after a synchronize or an import the operator
checks which group Database Settings names as the bin, `DESIGN.md` §138. *Measured:* on another
option's records and on generic carriers. On the store it was not measured.

**A rule is narrowed while a save runs** (row 15). *Who:* KeePass, where the plugin's group is
written before the node in the file. *What happens:* the file holds the older rule while memory
holds the narrower. *What covers it:* the plugin's group written last, and a node dialog that
does not close with OK while KeePass saves (evaluation 6). A rule is in the database from the
dialog's OK, like any edit made there (§629), so a save that begins after the OK writes it.
*Measured:* on another option's records, in a console process. With a rule that waits in the
database for the next save, in a real KeePass for a rule set at a dialog's OK pressed inside a
save of the plugin's own: that save's file lacks the rule, the database stays marked, and the
next save writes it (§644 item 6). A narrowing was not run (§644 item 11).

**Two files that hold the same entries of the store answer differently** (row 16). *Who:* every
fault but an overwrite, in a second model of the design. *What happens:* 3 pairs in 1,500
histories; the pair printed admits a client in a file and refuses it in the other. *What covers
it:* nothing found. *Measured:* in that model, before the decisions of 2026-11-16, and not
replayed on the store as decided.

**Closed by a later decision: rules changed apart at two nodes add up to more than either
copy's table gave.** It is kept among these because it ended wider until a version that lets
a profile through recorded what arrived (§617). *Who:*
nobody has to make a mistake. A copy takes a narrow rule off a node, or turns the node's
switch off, so that the profile gets there what arrives from above. Another copy, apart,
widens the rule above that node, at a time when the narrow rule, or the switch, still stood
there. Each table kept the node narrow for the profile. *What happened:* once the copies met,
the narrow rule was taken off and the rule above was the wider rule, since each is an object
of its own with a single standing version. The profile got at the node what the wider rule
gives, which neither table gave. The same held in the whole-rule form, since the rules of two
nodes met in neither form. This paragraph had nothing in the store cover it, as the cost of
rows and of nodes that do not meet, and left it to the project as what was left of S34. *What
closes it:* the version that takes the rule off, or that turns the switch off, holds what
arrived when it was written, and the node gives what arrives cut to that: what the first copy
gave (§617). *Measured without the record* (`measured-rules-per-profile` 7): on real
files, an empty rule taken off a node in a copy, while the other copy widened the root's rule,
gave the profile read and write at the node, where the first copy's table gave read and the
second gave nothing, in 16 of 18 cells, with a record for each node and profile and in the
whole-rule form alike. In the model, of 200,000 random histories over two copies, of acts on
rules and merges alone, a merge gave a letter that neither copy gave in 67 histories with a
record for each node and profile, and in 298 in the whole-rule form. The shortest of the 67
has the switch in the row's place: the switch is turned on in a copy and synchronized, the
other copy widens the root's rule, the first copy turns the switch off, and the copies are
synchronized. On files, 300 random histories of acts on a single row and on the switch gave no
such merge, and the 300 random histories of the store as decided gave a single such merge in
each form, after the authority's entry was deleted, which is a store started again (§641) and
no rule. With four profiles, the whole mask and a group between the root and the node
(`measured-rules-owed-forms` 3, 4, 8), the class was of the same order, 33 to 129 histories of
200,000, and wider in two ways that the sentence above does not name. A merge gave a verb
beside a switch of a rule that neither copy gave while each letter came from a copy, which a
check by letters does not see, on files in 16 of 16 cells of a sequence and in 1 to 8 of those
histories. And it reached a group between the root and the node, and a group's switch in the
place of a row, in 16 of 16 cells of each of three sequences. No merge gave more than both
copies at a single node. *Measured with the record* (`measured-rules-across-levels` 3, 4, 11):
none of those histories and none of those cells ends with more than both copies gave, by
letters and by capabilities (§617). *Held by* `StoreRuleMergeTests` over copies of a real
database that the product wrote: O1 to O8 and L1 to L10, and the sequences of the model's own
writes, each merged once by each method in each direction, end where those runs ended, and no
merge of them gives a capability beyond both copies; nor does any merge of the hand histories
H11, H13 and H20, of H1 and H3 split over three copies, of the switch in two inheriting versions
made apart, or of the press that settles after H1, H3 and H13, taken by the other copy.

**What the record of what arrived leaves: a narrowing above that reaches a copy in a single
direction and is widened again apart** (accepted, a later decision). *Who:* nobody has to make
a mistake. A copy takes a rule off a node, or turns its switch off,
so the node gives what arrives there on that day. A narrowing above then reaches that copy
from a second copy in a single direction, by a rule above, by the node's own switch or by a
move of the node. The second copy, which never received the first copy's change and still
holds the node closed, then makes wider again what stands above the node. *What happens:* when
the copies next meet, the node gives again what it gave when the rule was taken off: never
more than its own version recorded, which its tab showed at that press, and more than either
copy gave just before they met. *What covers it:* nothing in the store. The reading cannot
know that the first copy once gave less, since the narrowing is replaced and deleted by the
widening, and writing the narrower letters down when the narrowing arrives is a repair after a
synchronize, which cannot tell a narrowing that arrived from a later decision of a copy that
had seen everything (§630). The operator's documentation states that a node whose rule was
removed, or whose switch was turned off, gives a profile at most what reached the node when
that was done, until the limit is removed on its tab, and advises to synchronize both ways
before changing a rule above a node that another copy changed. A version that names what
stood above it closes it, by refusing, and was not taken (§617). *Measured*
(`measured-rules-across-levels` 4, 5, 8 and 13, the histories H4 and H5 there): in the model,
5 of 200,000 histories of 8 to 14 steps, each of this shape and each within what the node's
version recorded, against 294 without the record; none of 200,000 in any configuration of 4
to 10 steps without a move; a single history of 200,000 with a move in the alphabet, through a
group's switch; and on files a single sequence through a rule above and a single sequence through a
move, each ending so. *Held by* `StoreRuleMergeTests`, each pinned at the letters the measurement
gave and held to be beyond both copies, so that a change either way fails: H4 as the model's own
writes play it, H5 through a move with the last merge either way, and the history over three copies
that a search of the model drew, the last merge either way.

**What it leaves: two rules taken off apart, the upper of which gave what arrived anyway, and
a widening after the copies met** (accepted, the same decision; found by hand and replayed in
the model alone, in no run of the project's and on no file). *Who:* nobody has to make a
mistake. A group's rule gives a profile exactly what would reach the group anyway, or nothing
arrives for the profile at all, and a node below holds a rule of its own. A copy takes the
node's rule off, so the node follows the group. Another copy, apart, takes the group's rule
off, or turns the group's switch off, so the group follows what stands above it. *What
happens:* nothing changes when the copies meet, and no merge gives more than both copies gave,
so no search that holds a merge to both copies can count it. Every record holds everything
that arrives, so nothing cuts and no limit shows at the node or at the group, and the next
widening above both, made in a copy that holds both, carries the group and the node along. The
node then gives what neither operator's table had allowed it. Where the upper rule gave
anything other than what arrived at it, taking it off changes what arrives at the node, the
node's version cuts, and the case is closed. *What covers it:* nothing in the store. After the
copies met the store holds exactly what a single copy holds in which a single operator took
both rules off in turn, and there the nodes are meant to follow. The reading without the
record has it as well. The operator's documentation states that a node whose rule was removed
follows the group above it, and advises to look, before widening a rule, at the tab of the
nodes below it that have no rule of their own. *Held by* `StoreRuleMergeTests`, the model's own
writes of the sequence over two real copies, the node given the widening at the end.

**What it leaves: a limit that no longer cuts is forgotten** (by design; replayed in the model
alone). After a sequence that the record closed, an operator narrows the rule above in the
copy that holds both changes, so that the node below is cut no more, and later widens it
there. The write above carries the node, which gives the wider rule with no press at the node.
That is a later widening in a copy that holds the version, by an operator whose tab could show
the node. The limit rows at the node went when the narrowing was written, and nothing marks
the node as having been held back (§657). *Held by* `StoreRuleMergeTests`, after H1, the node
given the wider rule with no press at it.

**Closed by the build: a loss above, repaired, lifts every limit below.** It is kept among these
because it ended wider while a version below that recorded letters counted as following
wherever nothing arrived. *Who:* nobody has to make a mistake. A group grants a profile read and
list, and a node below holds a limit made apart: its rule taken off recording read alone. The
group's version is lost, so the group's row refuses and nothing arrives at the node, and the
operator sets the group's rule again. *What happened:* the node's version held everything that
arrived, which was nothing, and the write above renewed it with read and list: the limit was
gone by a repair, and not by a choice at the node. A box ticked and cleared above it did the
same. *What closes it:* where nothing arrived, a version below follows only where it recorded
nothing (§617), so the limit is left and goes on cutting, while a version that followed until
the box above was ticked recorded nothing at that press and follows the box cleared. *What it
costs:* a version that followed until a loss above is left as well, and gives what it recorded
until it is settled at its node, which is narrower. *Held by* `RuleActsTests`,
`GrantSheetWritesTests` and `GrantEditorInRealDialogsTests`; in no run of the model, which
renews such a version (§644 item 24).

**Closed by the build: a narrowing above misses a node while it lies in the recycle bin.** It is
kept among these because it ended wider while the tree a write above is carried down left the
bin out. *Who:* nobody has to make a mistake. Both copies grant a profile read at the root and
hold an empty rule at an entry in a group with no rule. A copy takes the entry's rule off,
deletes the entry to the recycle bin, narrows the root to nothing and restores the entry, and
gives nothing at the entry. The other copy, apart, gives the group read, and gives nothing at
the entry, whose empty rule answers. *What happened:* the narrowing did not reach the entry in
the bin, so its version went on recording read, and once the copies met the entry gave read,
which neither copy gave. *What closes it:* the tree holds the bin by its own place (§617), so
the narrowing reaches the entry there, and once the copies meet the entry gives nothing, which
both copies gave. *What it costs:* a renewal for each node in the bin that follows, written for
nodes the vault reaches nothing in, and a node restored under a group that gives more than
arrived by the bin's path comes back cut, shown on its tab as a limit. *Measured* over real
databases merged by `PwDatabase.MergeIn` with Synchronize, in each direction
(`GrantEditorInRealDialogsTests`); in no run of the model.

**What stays open of a move.** A write above reaches a node where it stands in the write's own
turn, the recycle bin among those places, and only through the versions the tab held. A node
with no rule of its own moved apart under a grant stays as row 9 has it. A narrowing above that
reaches a copy in a single direction by a move of the node stays as the class above has it. A
node moved out from under the dialog's node before the OK is not carried along, and a node moved
in under it is left and named in the log, since the tab did not hold its versions (§617). The
plugin's store stays out of the tree, as it stays out of the API (§633).

**A name is put back on the list apart from a row widened for it.** *Who:* nobody has to make
a mistake. A name off the list grants nothing, whatever rule still names it (§616). A copy
puts the name back, seeing what the rules give it. Another copy, apart, widens a rule for
that name, which changes nothing there while the name is off the list. *What happens:* once
the copies meet, the name is on the list and holds the wider rule, which neither copy gave it.
It is the sequence above with the list in the place of the node below, and the record of what
arrived does not touch it, since no rule changes when a name is put back. *What covers it:*
nothing in the store (accepted, the same decision). The Profiles page states, before a name is
put back, that every rule that names it is restored with it, and the operator's documentation
adds the rules another copy changed meanwhile, and advises to look at the rules of a name that
was put back after a synchronize. An addition that recorded what the rules gave the name is a
design of its own, neither drafted nor run. *Measured* (`measured-rules-across-levels` 8, H16
there): on files, in a single sequence, at what the client reaches. *Held by*
`StoreRuleMergeTests`, pinned at what a client holding the name reaches at the root, beyond both
copies, the merge either way.

**Closed by a later decision: rows changed apart at a single node, under a switch that
refuses.** It is kept among these because it ended wider while removing a row there took the
rule off, which is what that decision changed. *Who:* nobody has to make a mistake. At a node
whose switch refuses every profile it does not name, a copy removes a profile's row. Another
copy, apart, turns the switch off, at a time when that profile's rule there was narrow. Each
table closed the node to the profile, or kept it narrow. *What happened with the rule taken
off:* once the copies met, the profile's rule was taken off and the switch inherited, since
each is an object of its own with a single standing version, and the profile got at that node
what arrives from above, which neither table gave. In the whole-rule form the two versions of
the node's rule met, and the profile got nothing. *What closes it:* under a switch that
refuses, removing a row writes an empty rule (§617). Once the copies meet, the empty rule
stands beside the switch that inherits, and the profile gets nothing, which is what both
tables allowed. Removing that empty rule again while the switch still refuses writes nothing,
since taking the rule off there is the same sequence a step later. *Measured*
(`measured-rules-per-profile` 6, 7): written as an empty rule, the removal left the profile
with nothing at the node after the merge in 18 of 18 cells; written as the rule taken off,
the profile got read and write from the root in the 16 cells of 18 that are not imports. An
empty rule removed again and written as the rule taken off, beside a copy that turned the
switch off, ended the same way in 16 of 18 cells, and ended with nothing for the profile where
that removal wrote nothing. In the model, where the count above is 67, it is 68 with the
second removal taking the rule off, and 69 with every removal taking it off. The answer rests on an
empty rule never reading as a rule taken off. The two stayed apart through every merge and
every edit run, and the single exception runs the other way: after the loss of a version that
took a rule off, sealing the store again writes an empty rule, which is narrower (§617).

**That decision is reversed, and the sequence stays closed by the reading** (a later decision,
§617). The X takes the rule off under a switch that refuses as well, as `off:deny`, which lets
nothing through, so the removal in a copy and the switch turned off in another end with
nothing for the profile, as before. The counts of 67, 68 and 69 above are of forms that are
written no more. *Measured* (`measured-rules-across-levels` 3, 4): with the record of what
arrived and a real taking off, none of 200,000 histories and none of the cells on files of the
sequences at a single node gave more than both copies. A copy that holds the removal and
turns the switch off itself gives the profile what arrives, by that press (§617). *Held by*
`StoreRuleMergeTests`: W1 to W8 over copies of a real database that the product wrote, every merge
by each method in each direction, end where the model's writes ended, with nothing beyond both
copies, W6 at what that press gives in the copy that made it, and H9, the press in a single copy,
likewise.

**A 0.2 plugin serves a database that holds the store.** *Who:* a machine that still runs 0.2 on
the same synchronized file, or a plugin put back to 0.2, with a client that 0.2 still admits
and a rule that reaches the plugin's group, which inherits the rule of the root. *What happens:*
that client is served the records as ordinary entries, with `read` and the `protected` switch
the authority's private key and the store key, and with `update` it can change or strip the
plugin's fields (§654). *What covers it:* a warning and no code: the order of the move in §645,
which leaves a 0.2 plugin no authority to serve with, and granting on groups and not at the
root. No record of the store reaches a 0.2 plugin. *Measured:* read from the source of 0.2.0,
not run.

**The store's format number is lost in every carrier, and an older plugin reads on.** *Who:*
every version record that states the raised format and every summary entry that states it are
removed with no trace, by a script. A deletion that leaves a deletion record does not do it:
the deletion record tells of the version record and holds the plugin back (§623). *What
happens:* the store states a lower format number than it was raised to. An older plugin is then
stopped by any record of a kind it does not know (§621). It reads on only where the newer form
changed nothing but what a kind it knows holds or means, and where that change takes access
away, it ends wider. *What covers it:* the two carriers of the number, the deletion record that
tells, and the rule on a kind the plugin does not know, which needs none of them. What is left
takes a release that changes a kind and adds none, and the removal of every carrier with no
trace. Release 0.3.0 writes the first format, so no store has such a form yet. *Measured:* not
run.

### A stale copy that stays stale §640

**A newer copy is imported into a stale copy with Create new IDs** (row 6). This is not
fail-open. *Who:* a person who uses File, Import in KeePass on a copy of the same database and
leaves the preselected method. *What happens:* the newer file's records arrive as copies under
other identifiers, and the store reads only its own records (§621). The stale copy is exactly as
stale as it was before the import: the revoked client as admitted, the removed name as held and
the wider rule as much in force as before. Nothing gets worse. The import fails to bring the copy
up to date, and what can go wrong is an operator believing that it did. Until the copy is
synchronized, a gate on it is the gate of row 8. *What covers it:* Synchronize, which alone
brings a copy up to date, and the operator's documentation and the published list, which state
it. *Measured:* on real files, 10 of 26 Create new IDs cells, each into the stale copy
(`measured-decided-store` 2.1). The reports count those 10 as wider, since they hold every file
to every decision made at any gate, and so hold the stale file to decisions it never held.

### A store started again §641

**The single entry that holds the store key is left with no version that passes** (rows 12 and
19). *Who:* a person who deletes for good the authority's entry, the plugin's group, or, after a
Delete with no new authority, the retirement, where the deletion is then synchronized; or that
entry is edited an 11th time under KeePass's default history limit with no write of the plugin's
own in between, quick edits counted; or its content is edited a single time in a database that
keeps no history. Shift+Delete on the group asks a single question
(`measured-desktop-windows` 5.2). *What happens:* nothing is served in any copy the deletion
record or the edited entry reaches, and sealing the store again does not apply. Nothing is
granted by the loss. At the next Create the store starts again under a new key: every client
pairs again with the operator's approval, and every rule, profile name and revocation is entered
again.

**What entering again can leave out** (row 12). By itself the loss widens nothing, and with every
earlier rule passed over the store reads narrower. A client reaches more than before only through
what the operator enters again: a narrowing rule written under the lost key is no longer read, so
a broad rule entered again without that narrowing reaches a node the narrowing kept closed, and a
renewal that was refused under the lost key is asked again. Nothing in the file can show the
earlier narrowing.

*What covers it:* the loss is accepted for each of these causes, with the warning of §632
(decision 19). The plugin's next own write sets the entry back while a version of it still passes
(§628). For a deletion, a copy the deletion record has not reached still holds the store; for an
edit no copy helped, since the edited entry won either merge. The guard, while on, swallowed the
Delete key on the group. *Measured:* the loss of the key by deletion and by edits, and the
narrower reading after Create, on real files (`measured-decided-store` 3.5;
`measured-store-as-a-whole` 3.4). A rule set again after the loss was run in the model and not on
files (`measured-store-as-a-whole` 5). The group's deletion was run in the window, and its
reading is reasoning.

**The operator starts the store again with the command.** *Who:* the operator, on the KPG tab,
past the warning (§632). *What happens:* what the loss of the key does, by a decision and at
once: every client pairs again, and every rule, profile name and revocation is entered again,
so what entering again can leave out applies as above. The earlier store stays in the file and
counts for nothing, and no command takes the press back. *What covers it:* the warning, which
names what has to be entered again. *Measured:* not run.

### Refusing, narrower, or a decision lost §642

**The whole database stops** (row 17). *Who:* the newest summary entry is deleted for good, or
recycled and the bin emptied before the plugin writes again; or an entry of the store other than
the two that carry the store key is left with no version that passes, by any cause of §622.
*What happens:* every client is refused, in every copy the deletion or the edit reaches. *What
covers it:* the plugin's next own write, which sets an edited entry back while a version of it
still passes and can write nothing once the store has stopped; and sealing the store again
(§625). *Measured:* on real files (`measured-store-as-a-whole` 3.4; `measured-edited-refusals` 9;
`measured-decided-store` 3.2, 3.3; `measured-two-readings` 2.2, 2.3).

**Copies of the store's entries are made, then Delete Duplicate Entries is run** (row 18). *Who:*
an import with Create new IDs, a duplicate of the plugin's group with unchanged titles, or a
duplicate of the authority's entry; then Delete Duplicate Entries, which deletes the older of two
entries or whichever sits in the recycle bin. *What happens:* the originals are deleted and the
copies kept. The copies are not the store's, so no key is left and nothing is served, in that
file and in a third copy synchronized with it. What follows is the store started again of §641.
*What covers it:* the operation is named as unsupported (§637). *Measured:* on real files
(`measured-store-as-a-whole` 3.3, 3.6).

**Two authorities stand, neither retired** (row 20). *Who:* two gates that each create an
authority, or a merged file that brings its own. *What happens:* nothing is served until the
operator chooses, as today. *What covers it:* the chooser, `CA.md` §204. *Measured:* in the model
(`measured-store-as-a-whole` 2.4).

**A rule is lost after OK** (row 21). *Who:* KeePass ends, or the database is closed without
saving, after a rule was set in a node's dialog. *What happens:* the rule is gone, as any unsaved
edit is. *What covers it:* saving. Accepted (decision 11). *Measured:* in the model
(`measured-store-as-a-whole` 2.5, 5).

**An answer to a renewal is lost by a deletion** (§618). *Who:* a version of an answer and every
summary entry restating it deleted for good with their deletion records, or a deletion record of
an answer whose content no file holds arriving by a merge. *What happens:* the client is refused
forever, `409 forever`, where the lost version may have been a lift or an answer whose day had
begun. Narrower. A version settled by a lift's `lost` that a merge brings back with its content
stands beside the lift and refuses again: narrower as well. *What covers it:* a lift, which
names the loss in `lost` (§618). *Measured:* in
the model and on real files (`measured-renewal-answers` 4, 5).

**Two gates change the same profile's rule at a node apart** (row 22). *What happens:* after any
merge the profile gets what all versions allow, until the operator has removed versions in the
grant editor (§657). Narrower. Two gates that each remove a different version apart write
again what the other removed, and the operator removes versions again (§617). Narrower as
well. Two gates that change different profiles at a node end as decided, with nothing to
settle (§617). *Measured:* on real files, in the whole-rule form, where any two changes to a
node's rule met (`measured-store-as-a-whole` 3.1), and with a record for each node and
profile, for the same profile and for different profiles changed apart
(`measured-rules-per-profile` 4, 5).

**Two names whose tags meet share the place of their rows at a node** (§609). *Who:* nobody has
to make a mistake; two names meet by chance, about once in 65,536 for a pair. *What happens:* a
version of either row lost, its deletion record the only trace, refuses both rows, since the file
cannot tell whose it was. A row set or changed names in `after` the versions it replaces, which
answers for both rows, so an edit of either row refuses neither, and names in `lost` the lost
version it settles, which settles it for that row alone: **the other row keeps refusing until
it is set as well**. Narrower, and once each row at the place is set nothing refuses. *What stays:*
a merge that carries no deletion record (Overwrite if newer, Overwrite existing, Keep existing,
KeePassXC) carries neither the loss into the other copy nor the refusal of the other row, which
is row 2 for a rule: the merged copy gives that row what its own rules give, as the copy that
held the deletion record gives it in a reading that knows whose each version was. A Synchronize
carries both,
and takes the refusal of the other row away only where the merged copy holds the text of the
version the deletion record names. A lost version whose content turns up again stands beside
the version that settled it, and narrows the row. *Measured* (`measured-lost-settled` 3 and 4):
in the model, over two and three copies, a tag of 0, a person's deletions and Seal again, a
million histories with no row refused for another name's replaced version, no read wider than
the reading that knows whose each version was, and no merge beyond both copies that that reading
does not end at as well. *Held by* `StoreRuleMergeTests`, over copies of a real database that the
product wrote: two names whose tags meet under the copy's key, a version of the first row lost,
both rows refusing through every merge into the copy that lost it and by Synchronize, the first
row set again leaving the second refusing for the loss alone, and the second set again settling
it for itself, after which both rows give what they were set to in each copy the records reach.

**A node whose rule was taken off does not follow a widening made apart, or a move into a wider
group, until its limit is removed.** *Who:* nobody has to make a mistake. A rule is taken off
a node, or its switch is turned off, in a copy, and something above the node is widened in
another copy before the copies meet, also where that copy never held the node closed; or such
a node is moved under a group that grants more, in a single copy, where the plugin sees no
move. *What happens:* the node gives what arrives cut to what its version recorded, and the
tab shows the limit (§657). Narrower, and never a refusal for it: where the mask that arrives
shares no verb with the mask recorded, the profile gets nothing there. A group moved with
several such nodes inside needs a press at each. *What covers it:* the X on the limit row, a
single press at the node, and a later widening made in a copy that holds the version, which
carries the node (§617). It is the cost of the record of what arrived (a later decision).
*Measured* (`measured-rules-across-levels` 3 to 6 and 8, H11 and H13 there): in the model, a
copy gave less than the reading without the record, in a history in which that reading had no
merge beyond both copies, in 108 to 327 histories of 200,000 by the configuration, a profile
given nothing in 7 to 71 of them, and in 721 of the longer histories; on files, the sequence
in which the widening copy never saw the node closed gave read where the reading without the
record gave read and write, and a node moved into a wider group in a single copy the same. Of
the cells run by every merge both ways, none ended narrower than without the record but the
cells of the sequences it closes.

**A profile name is added in a copy and removed in another** (row 23). *What happens:* after every
merge that brings both copies' entries together, the removal wins over the addition that did not
see it, and the operator adds the name again. Narrower. *Measured:* in the model
(`measured-store-as-a-whole` 2.4).

**An admission or a profile addition is deleted, deduplicated, or does not arrive** (row 24).
*Who:* a deletion by hand, Delete Duplicate Entries after a duplicate, or Create new IDs into the
copy that lacks it. *What happens:* that client is refused or that name is off the list, until a
pairing or the name is added. Narrower. *Measured:* on real files
(`measured-store-as-a-whole` 3.1, 3.4, 3.6).

**A file brings a store key of its own.** *Who:* anybody who gets the operator to merge a file
that holds an entry which carries a key and passes under it; or, with nobody attacking, two
copies that each made a first authority, and a lost key that a merge brings back beside the key
of the store started again (§649). *What happens:* nothing is served until the operator keeps a
store. *What covers it:* the dialog that asks, and the record it writes, after which the key not
kept counts for nothing whenever it comes back. *Measured:* not run. Under the measured reading
the same file ends wider (§649).

**Copies keep different stores.** *Who:* two gates that each answer the question of §649 before
they synchronize, each keeping another store. *What happens:* once the copies meet, the two keep
records name each other's key, both keys stand, and nothing is served until the operator has
answered again. Nothing is lost. *What covers it:* the second answer, which names both earlier
answers and settles the question in every copy it reaches. *Measured:* not run.

**A store is raised above every plugin that serves it.** *Who:* somebody who writes under the
database's own key; or an operator who raises a store, past the warning, with a build that the
other machines do not have. *What happens:* every plugin with a lower format number serves
nothing for that database, in every copy the record reaches, and nothing lowers the number.
*What covers it:* the warning before the button, and updating the other machines. Where no
plugin of that format exists for them, the store is started again (§652). Refusing, never
wider. *Measured:* not run.

**A newer plugin serves a store that was not raised, and writes nothing.** *Who:* nobody has to
make a mistake: a machine is updated to a release that changes the format. *What happens:* until
the operator raises the store, that gate refuses every write into it: nobody is paired, renewed
or revoked there, and no rule is changed (§652). A gate with the earlier plugin goes on as
before. *What covers it:* the tab, which states it and holds the button. *Measured:* not run.

**A record of a kind the plugin does not know.** *Who:* a newer plugin wrote it, and the store's
format number did not hold this plugin back, because it was lost (§639) or because the build
that wrote the record had not moved it. *What happens:* the plugin serves nothing for that
database (§621). *What covers it:* updating the plugin. Refusing. *Measured:* not run.

---

## 12. Growth, and what is deferred §643

**The store grows without bound, and release 0.3.0 ships without pruning** (decision 21). The
operator's documentation states the growth, and the KPG tab shows the store's size (§636). No
automatic pruning rule is planned.

**What was measured** (`measured-store-as-a-whole` 3.8; the same sizes when run again as decided,
`measured-decided-store` 4), for a single gate with 10 clients, 5 names and 20 ruled nodes, in a
store in which nothing was written again:

| After | The summary entry: bytes of its strings, strings, earlier summary entries it names | Entries in the store | Longest single string | Deletion records | The file |
| --- | --- | --- | --- | --- | --- |
| 10 writes after the setup | 5,064; 27; 44 | 39 | 1,528 characters | 54 | 8,871 bytes |
| 100 writes | 11,951; 44; 137 | 57 | 4,598 | 222 | 16,039 |
| 1,000 writes | 81,551; 272; 1,144 | 285 | 37,830 | 2,008 | 90,167 |

**What grows.** The summary entry grows by an identifier with every write, since it names every
summary entry it replaces; by an item for every revocation, since none is pruned; and by the list
each standing version keeps of what it replaces. The deletion records grow by a record for every
replaced entry. An edited record grows by the history KeePass keeps, up to its limit, and a
record that the plugin set back holds its rewrites there as well (§628). **What an expired
certificate left stays until the compaction, and nothing is removed by a clock** (a later
decision): its admission, and the record that took it away. That is built for the admission: no
write deletes an admission for its age, and the clients page lists the admission of an expired
certificate as expired until the certificate is taken away, its client revoked or its authority
retired (§615, §627). *Held by*
`PairingInRealDatabaseTests.A_pairing_leaves_the_admission_of_an_expired_certificate_where_it_is`
and `MergeInRealDatabaseTests.An_expired_admission_a_merge_brings_stays_and_admits_nobody`.

**What a record for each node and profile does to the sizes** (computed from the strings the
measured code writes, with no file made, `measured-rules-per-profile` 9; the measured sizes
above are of a version for each ruled node). A store holds a standing version for each row and
for each switch that was set, where it held a single version for each ruled node, and the
summary entry holds an item for each of them. The run's 20 ruled nodes were 20 entries and 20
items; with four rows at each and the switch set they are 100 of each, in 22,480 bytes of
entries where the whole-rule form has 5,160. Over the cases computed, of 1 to 10 rows a node
with the switch, the same rules are 2 to 11 times the entries. A change in a dialog adds a
version for each row changed, and the switch, where it added a single version: a change to a
single row adds an entry as before, and a change to four rows of a node adds four. A row's
version is 256 bytes whatever the number of profiles at the node, where a version of a node's
whole rule is 268 to 322 bytes and grows with the profiles it names. So each version is
smaller by 12 to 66 bytes and no more, and the store grows by the number of rows. An item of
the summary entry is a string of its own of about 147 bytes, far below the reader's ceiling
for a string (§607).

**Those sizes are of the probe's texts, and not of the product's.** The 256 bytes are of a
version whose object holds a name of three characters, whose text holds a single letter, that
names a single identifier in `after`, and that holds the member `v`, which the product does not
write (§607). In the texts §617 fixes, by the same count, a row's version with a single
identifier in `after` is 245 to 316 bytes, for a name of 1 to 64 characters and for no letter
to every letter, and 33 bytes more for each further identifier it names (this document's own,
computed from those texts and not run). The sizes of a store the product wrote are owed (§644
item 24).

**The growth run, made again with a record for each node and profile** (measured,
`measured-rules-cut-parts` 3: the same single gate, clients, names, ruled nodes and 1,000
writes). After 1,000 writes the store held 352 entries and 208,394 bytes of its strings, where
the whole-rule form held 285 and 157,631; the summary entry held 103,745 bytes in 339 strings
and restated 336 items, against 81,551, 272 and 269; and the file held 2,527 deletion records
against 2,008. That is 1.24 times the entries and 1.32 times the bytes. The longest `after`
of a version named 33 identifiers against 36, since a row's versions name only the row's own.

**What the record of what arrived adds** (§617; computed from the texts, and measured where a
report is named). The mask adds at most 8 characters to a version that takes a rule off and
as many to its item, and `off:deny` adds 4. A switch's version that lets the walk go on holds
an item of at most 74 characters for each profile that something arrives for: the space, a
name of at most 64 characters, `=` and the letters. It adds no entry and no item of the
summary entry: a version that lets through is a standing version with its item already.
**A write above is what grows the store** (`measured-rules-across-levels` 9). A row of the
root widened above 20, 200 and 2,000 nodes that let its profile through wrote 21, 201 and
2,001 versions and left 22, 202 and 2,002 deletion records, each version below replaced by its
renewal in the same write, so no entry and no item more stands afterwards and a deletion
record more for each node. **A renewal grows a version's `after` as an edit of its row does**:
33 characters in the version and 33 in its item, for good. **`lost` takes 33 characters for each
lost version it settles and 9 for the member**, in the version and in its item, and nothing where
none is settled; every later version of the object carries it on (`measured-lost-settled` 6).
A row edited 400 times in a copy held 399 identifiers in 13,295 characters of content, and its
item 13,301 (`measured-rules-owed-forms` 7); a version below a row of the root edited 400 times was
renewed 400 times and held 401 identifiers in 13,361 characters, with three deletion records
left for each edit above. So a version reaches the plugin's ceiling of 262,144 characters at
about 7,940 identifiers, by its row's own edits and by the changes above it alike, with no
edit of its own where the rule above is changed often: computed from those strings, and
reached in no file. What a write does there is in §627. The item names of a summary entry are
not approached by renewals, a renewal replacing an item and adding none, and a write is a
single write whatever it renews, so the summary entry's own `after` grows by a single
identifier (read from the measured code).

**What an OK above many nodes holds KeePass's thread for** (measured by
`StoreReadingCostTests.What_an_OK_above_versions_that_follow_holds_the_thread_for`, explicit, in
Release, on a developer's machine, beside 10,000 entries in a hundred groups: an OK at the root
group above versions that follow, the median of ten after a first, each renewing every version
below, the median of three series). Above 200, 2,000 and 9,000 such versions the whole write
held the thread for 27.8, 321 and 1,824 ms, the worst of each 32.7, 387 and 2,067 ms. At 2,000
that is the reading in the writer's turn 81.5 ms, the composition with every renewal 141 ms, the
plan 65.3 ms and the application 22.0 ms; at 9,000 429, 595, 570 and 135 ms.
**The composition is mostly records**, timed part by part in a build that did so: at 2,000,
composing and checking a record for each renewal took about 110 ms, the rules of the reading 15
to 60 ms, the walk of KeePass's tree 18 ms, and the walks above each node with the names each
renewal carries about 32 ms, where they took 45 ms while the tag of `version` and the profile's
tag were worked out for each node and not once for the act. Opening the tab of that group,
which reads the versions below, took 11.6, 122 and 529 ms. A hold of the gate reading an entry
took 4.4, 31 and 171 ms beside those versions before any OK, and 4.6, 48 and 225 ms after the ten
OKs, beside the deletion records they left, a deletion record for each version each OK renewed.
**At a group below the root** (the same test, the group under the root holding the hundred
groups, a rule at the root and at the group, so that the act walks the group's parent for every
mask it records and the tab for what arrives, which an OK at the root never does; in the same
three series), above 200, 2,000 and 9,000 versions the whole write held the thread for 28.7, 305
and 1,695 ms, the worst of each 47.9, 347 and 1,906 ms, where the root took 27.8, 321 and 1,824
ms. At 2,000 that is the reading 74.9 ms, the composition 140 ms, the plan 51.4 ms and the
application 20.2 ms; at 9,000 449, 549, 553 and 113 ms. Opening that group's tab took 10.7, 119
and 506 ms, against 11.6, 122 and 529 ms at the root, and a hold reading an entry after the ten
OKs took 5.4, 39 and 217 ms. The walk above the group adds no more than two runs at the root
differ by, since the rules are worked out once for the act (§617).
The model's own figure, 113 seconds for 2,000 nodes, is of a model that reads its whole file
again for each node, and is no figure for the product.

**What every renewal costs, for good** (measured by
`StoreReadingCostTests.What_a_hold_and_an_OK_cost_after_this_many_renewals`, explicit, in
Release, on a developer's machine, beside the same 10,000 entries: a store composed as writes
above leave it, 2,000 versions below the root naming between them 0, 20,000, 100,000 and 400,000
renewals, and 9,000 naming 0, 90,000 and 360,000, each renewal an identifier at its own version's
place with a deletion record, in the order the writes leave them; the median of 21 holds after a
first, and of five OKs above those versions after a first). A renewal leaves a deletion record,
which KeePass keeps for good, and a name in the `after` of the version written anew, which only
grows, and every later hold passes over both: the two points of the paragraph above lie on a
line, and the line does not bend. **As first built**, a hold reading an entry beside 2,000
versions took 32, 103, 278 and 941 ms, with each version's deletion records together, an order
kinder to it than the writes' own: about 2.3 microseconds a renewal, so that it passed a second
after about 210 writes above those versions and five seconds after about 1,090. It wrote each
deletion record as text, read every `after` three times and built a set for each name a list
held. **Built again**, every `after` read once with the checked form of its version and kept
with it, each identifier compared as its bytes, and each deletion record answered by a search of
the lists in force at its place, the same holds took 31, 39, 55 and 134 ms: about 0.25
microseconds a renewal, the line a hold now follows. Beside 9,000 versions they took 120, 201
and 223 ms, about 0.29 microseconds a renewal over the run. So a hold passes a second after about
1,890 writes above 2,000 versions and about 340 above 9,000, and five seconds after about 1,890
above 9,000; above 2,000 versions it reaches about four seconds at the 7,942 writes a store has
room for (below), and does not pass five seconds there. **An OK above those versions grows
faster**, since it reads, checks, composes and writes the text of every name again, in each
version and in its item: above 2,000 versions the whole OK took 253, 365, 661 and 1,535 ms, about
3.2 microseconds a renewal, where it took 350, 507, 969 and 4,525 ms as first built, about 10.4.
Of the 3.2, the reading in the writer's turn, which checks every version afresh, is about 1.0,
the composition with every renewal 0.8, the plan 1.1, most of it the summary entry restating
every list, and the application 0.4. Above 9,000 versions the OK took 1,239, 1,694 and 2,948 ms,
about 4.7 microseconds a renewal, each whole OK applied once a series and the median of three
series. So an OK passes a second after about 115 writes above 2,000 versions and five seconds
after about 740, and above 9,000 versions it is past a second at its first write and passes five
seconds after about 90. What would take the line away is owed (§644
item 33).

**What a standing answer to a renewal adds, for good** (§618; measured by
`StoreReadingCostTests`, explicit, in Release, on a developer's machine, the median of 21 after a
first). For each client that was ever given a standing answer, a standing version and its item
in every later summary entry. As the product writes them, each naming a single identifier in
`after`, a version of `refuse:forever` holds 286 bytes in the store's strings, names and values,
a version of `refuse:until:` and a moment 305 and a version of `ask` 275, or 427, 446 and 416
with the entry's other strings, and the items that restate them 206, 225 and 195 bytes
(`What_an_answer_adds_to_the_store`). Each further answer about the client adds 33 characters to
the `after` of the version and of its item and leaves a deletion record for the version it
replaces (computed from those texts), so `after` reaches the plugin's ceiling at about 7,940
answers about a single client. Nothing of it is pruned. **What it adds to a hold**
(`What_a_connection_s_hold_costs_beside_standing_answers`): beside 10,000 entries without
history, the store the growth run left and a single admission, whose client is among those
answered, the hold a connection is judged in took 2.75 ms beside no answer, 3.40 ms beside 100
clients each holding a standing answer given once and 9.53 ms beside 1,000, the worst 4.22 and
14.1 ms; each of those answers given ten times, each naming the versions before it, which left
a deletion record each, 3.74 and 12.5 ms, the worst 28.2 and 41.2 ms. So a standing answer
costs every hold about 7 microseconds, and each answer it replaced about 0.3 to 0.4, the line
renewals above follow; the hold parses no text. **What its write holds KeePass's thread for**
(`What_an_answer_s_write_holds_the_thread_for`): the turn of the writer in which the renewal
dialog's refusal forever is written and saved (§618), each refusal about a client of its own
beside 10,000 entries and an authority, took 5.7 ms at the median and 17.9 at worst for the
store's own part, which is the reading with every entry, the act, the plan, the entries asked
about and the application, with a save that writes nothing and the file hashed back; and 124 ms
at the median and 143 at worst with KeePass's own save of the database, which the answer's
write makes. With ten history items on every entry, 34 and 57 ms for the store's own part,
and 1.17 seconds at the median and 1.23 at worst with the save, each figure the median of
three series. Beside a pairing's write in the same series, the store's own part
took 5.7 ms against the pairing's 7.1, and with the save 124 against 125 ms, 1.17 against 1.13
seconds with the histories: the answer's write costs what a pairing's does, and its save is
what grows, which no sum of the plugin's timeouts bounds (`DESIGN.md` §119).

**How many ruled nodes the 9,999 item names of a summary entry hold** (S13; the same
computation). With a record for each node and profile, 1,999 nodes of four rows each with the
switch set fit, and 2,000 do not, by a single item: they are 10,000 items. Without the switch,
2,000 nodes of four rows are 8,000 items and fit. With ten rows each and the switch, 909 nodes
fit. In the whole-rule form a node is a single item whatever its profiles. This corrects the
sentence that stood here, by which a database with 2,000 ruled nodes of four rows each did not
fit: it does not fit only where the switch is set at every node.

**What the kinds that were not measured add** (reasoning; in no measured size). Each record that
takes a certificate away leaves an item in every later summary entry (§648), so a certificate
that several gates took away apart leaves an item for each of them. The reference client
gives the old certificate up after every renewal, so every renewal adds an item, for good. A
summary entry has names for 9,999 items (S13): fifty clients that renew every week add 2,600
items a year and reach that in under four years, and a single client that renews every day adds
365 a year. Nothing in 0.3.0 limits it. What limits it is the compaction deferred below. A keep
record leaves a record and an item for each answer (§649). A version record is left for each
format and for each release that stated itself, and the summary entry states a single pair
(§650).

**How long a string a program keeps.** The longest string at 1,000 writes is 13 times the longest
string any run on an emulator carried. Keepass2Android and KeePassDX, each as its own code
compiled for the desktop, kept strings of up to 262,144 characters through load, save, edit and
merge (`measured-source-harness` 4.1); on a device that was not measured (§644). The plugin's
own reader has ceilings as well (§607).

**The plugin's own ceilings are below its reader's** (S13). The plugin writes no string of the
store longer than 262,144 characters, the longest string measured to come through the listed
programs, and no item past `KPG.S.9999`. Its reader takes a string of up to 1,048,576
characters (§607). The distance between the two is what keeps growth from ever reading as
damage: records that each fit the first ceiling are each read, however many a merge brings
together, and only the next write, which names them all, has to fit again. A summary entry's
`after` holds 33 characters for each summary entry it names, so it reaches 262,144 characters at
7,943 summary entries named and the reader's ceiling at 31,775. The content that holds it,
beside a height of four digits and a nonce, passes the plugin's ceiling at 7,942 named (§627),
which is about that many writes of the plugin's own into a store.

**The operator is told from half of either ceiling.** From a summary entry whose `after` holds
131,072 characters, which is about 3,970 writes, or which holds 5,000 items, the log states at
every write how far the store has grown and what it has room for, and when a reading first finds
a store it serves there, and the KPG tab states it beside the store's size (§636). That is the
condition S13 set: the store does not reach a ceiling before the operator has been told for as
many writes again.

**A write that would pass a ceiling is refused whole, before anything is added**, since no
listed program was measured with a longer string, and a record beyond the reader's ceiling
would be damage in every copy it reached. It is judged where the write is built, so the pull
request that moves the first write onto the store builds it. *Held by* `StoreWriteTests`, with
that write: a write that fits is written; the same write a character or an item beyond is
refused and leaves the database as it was; and two stores that each fit, synchronized, are
judged by what the next write would name (§627).

**A store that has no room for its next write is served nothing for** (a later decision). The
gate answers every request for that database `stopped` (§656), and serves nothing until the
store is started again (§632). The press that seals a store again does not lift it, since the
press is a write and needs the room that is missing (reasoning, §625). The command that starts
the store again still works there, since what it writes is under a new key and names nothing
of the earlier store's summary entries (reasoning, §632). This was the provisional form while
S35 stood open, the most restrictive of the answers that item held. The reading judges the room
at every hold, after every condition of §624, and the log gives it as the stop's reason; the
refusal of a write that would pass a ceiling comes with the pull request that moves the first
write onto the store. *Held by* `StoreRoomTests` and `StoreInRealDatabaseTests`.
**Considered and not taken:** a gate that keeps serving and refuses every further write into
that store, so that nobody is paired, renewed or revoked there until the store is started
again; and the same with a part of the room kept for writes that only take access away, so
that a revocation still fits when a pairing no longer does.

**That answer is looked at again once a compaction exists** (the same decision). Until then a
store started again is the only way back to room, and with a compaction there is a second,
which may change what the gate does with a store that is full. How soon a store gets there
turns on how often the plugin writes (reasoning): a renewal is two writes where the client
gives the old certificate up after it (§648), so ten clients that renew every week make 1,040
writes a year and reach 7,943 in over seven years, and fifty such clients make 5,200 and reach
it in about a year and a half. A database that busy needs the compaction before then.

**Deferred to after 0.3.0: a compaction by hand when the store is sealed again.** The press would
then also write the store fresh and drop the lists, and a copy that is behind would refuse until
it is synchronized. That compaction is neither designed nor measured. What it would have to
guarantee (reasoning, evaluation 8): a copy that is further behind than the compacted store
reaches ends refusing until it is synchronized, and never wider; it needs no knowledge of which
copies exist or of what each has seen, since the file holds no memory of a single copy and
nothing is kept outside it; it does not depend on a clock; it holds with several gates compacting
apart; and it leaves a store that Database Maintenance has cleared of deletion records no worse
than before. The design paper's cut of the lists to 64 is the measured warning: for a copy 65
writes behind, a deletion record far below the list was taken for that of a replaced summary
entry, and the copy ended wider in the model.

---

## 13. What is not measured §644

Each of these is an obligation of the pull request that builds the part it names. A pull request
that finds a measurement against a decision reports it and does not design around it.

1. **The API against a composed store**, which is the condition of §633: every route, with every
   right a rule can give, against a database that holds the store, with entries in the bin and in
   other groups, copies under other identifiers, and the reference client following references.
   Run by `StoreOnEveryRouteTests`, over records the test composes, and holding on every route
   declared; §633 states what it does not show. Run again with the rules in the store: a rule
   the product wrote grants everything at the root, and rules composed for the plugin's own
   group and for a record of it, which the product writes for no node of the store, reach
   nothing on any route. Owed again by every pull request that adds a route
   or a kind of record, whose case it has to bring. *Could overturn*: the authority's key in an
   entry.
2. **What the run of the store as decided left out** (`measured-decided-store` 8): sealing the
   store again after a stop by an edited entry; on a store with summary entries, an edit in both
   copies, an edit by pykeepass, a history item changed by a script, Database Maintenance's
   history page, and KeePassLib's other methods and the other KeePassXC releases for an edited
   entry; a rule set again after the loss of the entry that carries the key; an edit that leaves
   a record's subject intact, three copies and chains of merges; and the model with an entry's
   history, which it does not hold, so that the history rule was run on files only. The pull
   request that builds the reading runs, over real databases, an edit, the content removed, ten
   edits and the eleventh, a quick edit and a move, a database that keeps no history, the
   authority's entry edited past the limit, and Synchronize carrying a deleted summary entry
   (`StoreStatesInRealDatabaseTests`). Sealing the store again after a stop by an edited entry
   was run in the model and on real files before the seal was built (`measured-seal` 5): each
   kind of entry edited past the limit, by the dialog and by quick edits, and with no history
   kept, then the seal, then each merge of KeePassLib and KeePassXC 2.8.0 both ways; the seal
   applied wherever the key was held and left the store whole, and no file granted beyond its
   ideal. The product's seal is held to its sequences (`StoreWriteShapeTests`, R8 to R12). The
   rest of this item stays owed. *Could overturn*: the history rule as worded, and what §632
   states of a store started again.
3. **The record written again, beyond the run that was made** (`measured-two-readings` 5): in
   KeePass's window and by the plugin itself; beside a second KeePass with the plugin; on a
   phone or an emulator; with KeePassXC releases other than 2.8.0; with the slow key derivation;
   with a history limit other than 10 items and none; and in the searches of the design's
   model. Writing a record again as a new entry beside the edited entry was not run either, and
   is not the form decided. The build of §628 brings the case in KeePass's window, a record's
   content deleted in the entry dialog and set back by the next OK on the KPG tab, as
   `LiveStoreSetBackTests`; every other case above stays owed. *Could overturn*: decisions 18
   and 24.
4. **The guard as product code in KeePass's window**, with its setting: a change made in the
   group dialog of the plugin's group; a delete or a duplicate of a group above the store; "Yes"
   at the save question of the dialog for several entries; a click on OK itself; paste between
   two databases; and XML Replace, which was read and never run. The whole set of cases was not
   run again in a single run under the final guard. Owed by the pull request that builds the
   guard. *Could overturn*: nothing in the store, since the guard protects nothing.
5. **The store's summary entries on a device**, through Keepass2Android's save, edit and merge: a
   summary entry with a long list and a string of tens of thousands of characters, and what an
   edit of a record leaves in its history there. No run on a phone or an emulator carried a
   summary entry. Owed before the published list names Keepass2Android for the store. *Could
   overturn*: criteria 1 and 3 for Keepass2Android.
6. **A rule that waits for the next save**, on files and in the window: the dialog's OK while a
   save runs, a save that is answered with Synchronize and so saves twice, and KeePass ending
   before a save. Owed by the pull request that builds §629, with what §626, §627 and §629
   state by reasoning of a node dialog's close: that it is raised once a save pumping under the
   dialog has returned, that the dialog that closes holds nothing the write touches, and that
   the database stays marked as changed after it. *Run* with the real `GroupForm` and
   `PwEntryForm` and the real writer, outside a running KeePass: an OK pressed inside a
   pairing's save is written once that save has returned
   (`NodeDialogOkBesideSaveTests`), and an OK writes the rule with the entry unchanged and the
   database marked, while Cancel writes nothing (`GrantEditorInRealDialogsTests`). *Run* in a
   real KeePass 2.62, with the product's own write at the dialog's close and a client of the
   gate: the OK of the group, entry and Add Entry dialogs in force for the client before any
   save, the database marked, the entry dialog's with nothing of the entry changed, the
   operator's save writing the rule, and Cancel and Escape writing nothing
   (`LiveGrantEditorTests`); a save over a file another copy replaced, answered Synchronize,
   which writes both copies' rules, Overwrite, which writes the window's and loses the other
   copy's, and Cancel, which writes nothing and leaves the database marked until a later save;
   KeePass asking before it closes, locks and exits with a rule unsaved, Save writing it; the
   rule saved with its entry under automatic saving after an entry's dialog; and a database
   closed with its changes discarded once KeePass asked, which leaves the rule out of the file
   (`LiveRuleWaitsForTheSaveTests`); and an OK pressed inside a save of the plugin's own under a
   key derivation of seconds, the dialog closing once that save has returned, the save's file
   holding its own change and not the rule, the database marked and the rule in force until the
   operator's save writes it (`LiveRuleOkInsideASaveTests`). Nothing ran otherwise than this
   item and §629 have it. *Still owed*: KeePass ending without its close handling, where a rule
   is lost as every unsaved edit is, and an OK handled while a save serializes the tree rather
   than while it derives the key. *Could overturn*: decision 11 as worded.
7. **Two KeePass instances with the plugin** on a single synchronized file: a revocation at the
   first, a renewal at the second, a rule changed at both. Several gates were run as files that
   each write.
8. **KeePassXC's window in the releases other than 2.8.0**, and in 2.8.0 its lock, close and Save
   As over a changed file and its direct-write setting. *Could overturn*: which releases the list
   names.
9. **Keepass2Android beyond the runs made**: a long time with the screen off, a kill in the
   middle of an upload, the periodic and Wi-Fi-only options, servers other than WebDAV and SFTP,
   and a device that is no emulator.
10. **A real file synchronizer** between two writers, with a conflict made on purpose. *Could
    overturn*: what the list has to state beside the first sequence of §639.
11. **The sequences of §639 that were not replayed on the store**: a node's place decided by a
    merge, a merge that makes another group the recycle bin, a rule narrowed while a save runs,
    the differing files of row 16, and a deleting device whose clock is behind, on real files.
12. **The reading on KeePass's thread.** Measured, with a change's turn and with a key for each
    entry of a file, by the pull request that builds the reading, through the real router and
    vault, and recorded in §620, in `DESIGN.md` §119 and in `FINDINGS.md` section 22: it
    overturns nothing. Measured as well with the admission of a connection read by meaning, at
    the ceiling of statements, along a chain of renewals as long as that ceiling, and for a
    pairing's write with its save (§620, §627): it overturns nothing either. What stays owed is
    the same run on a machine that is not a developer's, and a database of more entries than
    10,000.
13. **The page that seals the store again**: what it shows after each kind of loss. What it can
    show, the listing of §625, is held to what `probe/read.py` listed for each kind of loss
    (`StoreLossesTests`). Built as the dialog Seal store again opens (§625), which has a row for
    each loss of the listing and none for a record no summary entry restates or for a condition;
    the press in a real KeePass is the end to end tier's, `LiveSealTests`.
14. **Which of KeePass's windows takes back or takes away an entry of the plugin's group** while
    the plugin writes (§626). This item is this document's own, read from `DESIGN.md` §124 and
    from no report. Run by the pull request that keeps the authority in the store, in the host
    tier (`StoreWritesUnderDialogsTests`): each window of §124 read off the real types, and the
    entry dialog's copy put back over a write reproduced; §626 and `DESIGN.md` §124 state what
    it found. A window shown in a real KeePass over a write stays owed by the end to end tier.
15. **A second store key** (§649), of which nothing was run. The case to run first is a file
    from outside that holds a retirement carrying a key of its own and naming no authority of
    the database, a rule version under that key for a node with no rule, and a summary entry
    under that key that restates both; and beside it the same file with an authority of its own
    and a retirement that names the database's authority. Each brought in by Synchronize, by
    each import method that keeps identifiers and by KeePassXC, read before the operator
    answers and after the answer. Then two copies that each made a first authority; copies that
    keep different stores and are asked again; a later answer that takes an earlier answer
    back; a lost key that comes back beside the key of a store started again; and a file from a
    holder of the database's key with a keep record that names it. The pull request that builds
    the reading runs a file that brings a key of its own by Synchronize and by each import
    method that keeps identifiers, and copies that keep different stores, over real databases,
    and the keep records, their items, their deletion records, circles and chains over composed
    ones; KeePassXC, the reading after an answer, and the rest stay owed. *Could overturn*: how
    the decision on a second key is built.
16. **A single certificate taken away** (§648): a renewal and the old certificate given up at a
    gate while another gate holds a copy from before; a renewal made from that certificate at
    the other gate in the meantime, and a renewal onward from that, also once the certificate
    between was given up at that gate; the same certificate taken away at two gates that had
    read other renewals; the operator's single removal; the record deleted and edited by hand;
    an admission whose parent admission is lost; a revocation of the same client beside it; and
    each merge. Run for KeePass's own merge, each of its methods in both directions, over real
    databases through the product (`StoreAdmissionMergeTests`, §648): it overturns nothing,
    and it measured which merge brings the written version of an edited record back (§622).
    What stays owed is the same merged by KeePassXC, between two KeePass instances with the
    plugin, and on a device. *Could overturn*: the rule about renewals (S29).
17. **Create refused on a stopped store** (§624), which the measured code does not do. Run by the
    pull request that keeps the authority in the store, over a composed store
    (`AuthorityCommandsInStoreTests`): refused, and nothing written. The tab offers no command under
    a word, so nothing of it is run in KeePass's window.
18. **A version that has to read a single way** (§607, §608): a string beginning `KPG.S.` that is
    not four digits, a line feed in a part, a member that appears twice. Each fails in
    `StoreReadingTests`, over composed versions; none was run through another program's save.
19. **The format number, the version record and a kind the plugin does not know** (§647, §621), of
    which nothing was run: the highest of each after each program's merge; an older plugin on a
    store that was raised; a newer plugin on an older store, before and after the operator raises
    it; a write refused in between; a deletion record that tells of a version record; a record and
    an item of a kind the plugin does not know; a gate on a stale copy; and a store started again
    from a store that holds the plugin back, with the earlier store coming back by a merge. The pull
    request that builds the store's core runs, over real databases, a version record of a later
    format and its deletion record arriving by Synchronize, and a record of a kind the plugin does
    not know with no version record present; and over composed stores, the item, the deletion record
    that tells a number and one that does not. The pull request that keeps the authority in the
    store writes the version record at a store's first write and at a write by a later plugin, and
    at no other (`StoreWriteTests`). The rest is owed by the pull request that builds the operator's
    raising of the format (§647).
20. **The move from 0.2** (§645, §654), which was read from the source of 0.2.0 and not run. The
    steps, and the path through KeePass's windows for clearing what 0.2 left, are run in a real
    KeePass by the pull request that writes the warning.
21. **The command that starts the store again** (§632), of which nothing was run: pressed on a
    whole store, by a plugin that the store's format holds back, and by a plugin that does not
    read the store's form; then the earlier store brought back by each merge, and a second gate
    that still serves the earlier store. Owed by the pull request that builds the command.
22. **The words of §656**, of which nothing was run: each state answered with its word, to a
    request before a route is chosen and to a `head`; `frozen` to a write and to no read; and
    whether `lost` and `outdated` can be sent at all (S33). The pull request that builds the
    store's core runs `split`, `outdated` and `stopped` over a real handshake to a request and to
    a `head` (`StoreHeldBackGateTests`), and `stopped` in a real KeePass (`LiveStoreTests`).
    The pull request that keeps the authority in the store runs `lost` over a real handshake and
    from a key carrier deleted in a real database (`HeldBackGateTests`,
    `LockInRealDatabaseTests`). The pull request that keeps the admissions in the store runs,
    over real databases, what a request in flight and what a database was last served with
    answer once several authorities stand, with the request's own authority among them and
    without it (`LockInRealDatabaseTests`). `lost` in KeePass's window and `frozen` stay owed,
    `frozen` by the pull request that makes the gate answer it.
23. **The grant editor's rows for a rule in several versions** (§657). The pictures the decision
    was taken from are of a prototype with rows that were made up, and not of a store: two and
    three versions, scrolled, under a high contrast setting, and with what removing a row and
    the dialog's OK write. Owed by the pull request that builds the editor on the store. *Run*
    on a shown form and in the real group and entry dialogs: the rows of a rule in several
    versions, of a limit and of a lost rule over rules of the store, what each X and the
    dialog's OK write, and the room of the tab at each scaling with every text above the
    table in at most two lines (§657). *Run* on a shown form, read off its pixels, and in the
    room of the real dialogs at each scaling: the bracket, the colors, the bold letters and the
    highlight of what changed, and every drawn state with no color, which is what a high
    contrast setting leaves (§657). The pictures are taken of that panel by
    `GrantPanelGallery`: two and three versions bound with their letters in bold, a version
    that takes the rule off among them, a row changed by each press, a version removed with the
    rows left bound and changed at once, the longest texts, a group cut by scrolling, and every
    drawn state with no color. *Run* in a real KeePass, read through UI automation: the rows
    of a rule in several versions, out of view and scrolled into view, and of a limit, each
    State cell's word and help text, the help text of a line a press changed, and what the X on
    a version, the X on a limit and the dialog's OK write (`LiveRuleSeededStatesTests`). *Still
    owed*: a real high contrast setting, and the pictures of the rows in a real KeePass.
24. **Rules as a record for each node and profile** (§617). This item held the run owed before the
    rules are built: the run of the store as a whole, made again with a rule record for each node
    and profile and the switch as a record of its own. It was made (`measured-rules-per-profile`;
    the numbers in parentheses below are its sections), on files made and changed by KeePassLib 2.62
    and merged by KeePassLib and `keepassxc-cli`, every part twice with the same output: the merge
    cells of the store as decided, over KeePassLib's five methods and four KeePassXC releases, each
    also in the whole-rule form; the same deletions and edits of an entry of the store, with the
    switch as a further kind; the same random histories, and random histories of acts on a single
    row and on the switch; and the model's searches with that form. **What it showed**, by what the
    item asked for. No cell is newly wider: of 396 merge cells 16 ended wider, the same cells in
    both forms, each an import or a stale copy, and of the 724 files of the random histories no file
    ended wider with a record for each profile that did not in the whole-rule form; the class of a
    single merge cell and of six of those files differs between the forms, and none of them grants
    more (3). Two copies that change different profiles at a node ended as decided with no row to
    settle, in 112 of 112 cells that are not imports, where the whole-rule form ended narrower in 80
    of 96 (4). The same profile changed apart gave what all versions allow, for two versions and for
    three (5). An empty rule and a rule taken off stayed apart through every merge and every edit,
    and not through every loss: after the loss of a version that took a rule off, sealing the store
    again writes an empty rule, which is narrower (6). The switch in versions made apart refused, in
    the single sequence run (7). A deletion record still told which row's version was deleted (8).
    Rows changed apart added up to more than either copy gave in 67 of 200,000 histories of the
    model, against 298 in the whole-rule form, and a row removed under a switch that refuses,
    written as an empty rule, did not (7; §639). The model's searches failed in the same kinds as
    for the store as decided; their counts are the earlier record's in 20 configurations of 24 and
    differ by a single failure in each of the other four, in histories that were not traced; and
    with acts on a row and on the switch, and no loss of the authority's entry, no shortest failing
    history holds a row or a switch (3). **What that run left owed, and what was run of it since.**
    This item held as owed more than two profiles and two letters in a file, with the switches of a
    rule; a rule on a group between the root and the node; a row of a name off the list whose
    records and items are all gone; two profiles whose tags meet, in the form proposed for S8; how
    the `after` lists grow under repeated edits of a row; the histories in which the model's counts
    differ; and the parts cut from that run, with the other KeePassXC releases. Each was run, in the
    same probe family, twice with the same output. **`measured-rules-owed-forms`**: four profiles
    and the whole mask, where versions of a row made apart gave exactly what both allow in
    16,974,336 of 16,974,336 combinations and no merge on files gave a verb or a switch beyond both
    copies at a single node (§617); three levels, where nearest wins through versions made apart at
    different levels in 16 of 16 cells of each sequence, and the class across nodes reached the
    group between and its switch (§639); a row of a name off the list, where nothing was wider that
    the list causes (§617); two names whose tags meet, where the form of S8 is supported with a
    reading that tells of every name of a tag and the reading as first run is overturned (§609,
    §617); and the `after` lists, 33 characters an edit and nothing pruned (§643).
    **`measured-rules-cut-parts`**: the edits, the deletions and the random histories of rows with
    each of four KeePassXC releases, with no cell of 1,128 in another class under another release
    and none wider in a merge by any; the cells under the slow key derivation, the duplicates,
    Delete Duplicate Entries, the growth and the replays, with every change of class between the
    forms going from wider to refusing (§617, §639, §643). The four histories in which the model's
    counts differ are traced: in each, the forms part at a rule act that changes no row, which
    writes nothing with a record for each profile and a version of the node's rule in the whole-rule
    form, and that version, waiting in the model's memory for a save, has the model refuse the hand
    deletions that follow. It is a property of the model and no matter of a rule: with that act
    taken out both forms answer alike, and the three histories that fail with a record for each
    profile alone are a store key lost, twice, and a retirement deleted with its summary entry.
    Beyond the shortest histories, a single failing history holds an act on a row: a row's narrowing
    deleted with a summary entry, which is row 3 of §639 and fails alike in the whole-rule form.
    **`measured-rules-across-levels`**: the record of what arrived and the form considered beside
    it, in the model's draws of each configuration, in longer histories, with a move of the node, on
    files in every sequence that had ended beyond both copies, and for the cost of a write above
    (§617, §639, §642, §643). **What stays owed**, by the pull request that builds the rules, over
    real databases the product wrote unless a clause names another place: the sequence that
    `off:deny` is for, with nothing arriving for the profile, which the run on files made with read
    arriving (§617); the press that settles, and the removal of a single version of a rule above, on
    files and in a search; three copies; a move of a group with its nodes, a move under an open
    dialog, and a move by a merge other than Synchronize; a merge that lands under an open node
    dialog, and the two orders of an X and the check box on a single tab, which a model that
    composes at its write cannot run; a version that lets through, and a renewal of it, lost with
    and without the summary entry and a deletion record, and edited past the history limit, with the
    new texts; a text out of form; the loss of every row of a node at once; a row set again after it
    refuses (§657); more than four profiles; a tree of more than three levels on files; the read in
    the plugin's own code, with what a hold costs beside a deletion record under the tag of
    `version` that nothing answers for (§620); the sizes of §643 in the product's texts; and the
    grant editor, a second KeePass with the plugin, and a device (items 23, 7 and 5). What a write
    above 200, 2,000 and 9,000 versions holds KeePass's thread for, and the opening of a tab above
    them, is measured, at the root and at a group below it (§643): within the five seconds a hold
    is let run for a store's first writes above them, and not for good, since every renewal adds
    to every later hold and OK; §643 has the line each follows and the writes after which each
    passes five seconds, and item 33 what would take that growth away. *Could overturn*: the
    conditions of the write above as §617 has them for a dialog. **What the build of the rules ran
    of it, and what it leaves** (§617, §620, §629). Run: the reading held to the meanings of the
    measured code, each store read by letters and by capabilities; the acts of an OK held to the
    writes of the measured code step by step, the writes above and their renewals among them, with
    the histories §639 keeps; a text out of form; a lost row and a lost switch; the two orders of an
    X and the check box on a single tab; a version that reached the database under the tab, in
    memory; over real databases the product wrote, rules changed apart in two copies and merged by
    `PwDatabase.MergeIn` under each method that keeps identifiers, in each direction, ending at no
    more than either copy gave, with Create new IDs leaving each copy as it was; an OK on a real
    group dialog carrying a widening and a narrowing into the versions below that follow it,
    leaving a limit, a version below that `PwDatabase.MergeIn` brought in under the dialog and a
    node moved out from under the group before the OK; and a node in the recycle bin carried along
    a change above, with the sequence of §639 through the bin over real databases merged by
    `PwDatabase.MergeIn` in each direction. **Run over synchronized copies since**
    (`StoreRuleMergeTests`, over real databases the product wrote, every rule written as a node
    dialog's OK writes it, each merge held to both copies by capabilities and followed by a write
    of the plugin's own that touches no rule and leaves every answer as it was): every sequence of
    rules of `StoreWriteVectors.json` over two copies, the answer after each step the model's and a
    merge beyond both copies exactly where the model's was; those of them that end with the copies
    met both ways, W1 to W8, O6, O7 and L8 to L10 among them, merged once instead by each method in
    each direction, also with the entries of either copy dated later, ending where the model's
    ended; O1 to O5, O8 and L1 to L7 with four profiles and the whole mask, likewise, two of them
    over three copies; H11, H13, H15 and H20, the press that settles after H1, H3 and H13 taken by
    the other copy, H1 and H3 split over three copies, and the switch in two inheriting versions
    made apart; the classes §639 keeps, pinned; the sequence `off:deny` is for, with nothing
    arriving; an empty rule, a rule taken off and no rule through every merge; a version that lets
    through and a renewal of it deleted with and without its summary entry and a deletion record,
    edited within the history limit and past it, and duplicated; a row that refuses for a lost
    version, set again on the tab and merged back; two names whose tags meet, a name whose tag is
    0, and a lost version of a name the file does not know; and a node that holds a version that
    lets through moved into a wider and a narrower group, a group moved with two such nodes, and a
    node's place decided by a merge. Owed to the model: the write above where nothing
    arrived, which the build renews only for a version that recorded nothing, and the measured
    code for every version (§617); no sequence of `StoreWriteVectors.json` holds that state at an
    act above, so no vector differs. The lost versions a version settles, named in `lost` apart
    from the versions it replaces in `after`, are built as the model has them
    (`measured-lost-settled` 6, §617), and the build reads every store and plays every sequence
    of the vectors of that refinement as the model printed them: the fail-open sequence, edits
    of names whose tags meet, a loss settled in each order and in a single write, a tag of 0
    beside the object with no slash, and a lost version of the switch. **Still owed** of the list
    above: the removal of a single version of a rule above, over copies; the press that settles
    and that removal in a search; a move under an open dialog, and a move by a merge other than
    Synchronize; the loss of every row of a node at once; more than four profiles; and a tree of
    more than three levels. Left to a later commit: a merge under an open dialog in a running
    KeePass, and every cost but that of the write above. The drive in a real KeePass was run, as
    items 6 and 23 have it.
25. **What the Profiles page counts** (§636). Its count of the groups and entries holding a rule
    for a profile, `DatabaseNodes`, leaves the recycle bin out and not the plugin's store, which
    the API leaves out beside it (§633). This item left it to the pull request that brings the
    tab's listing of the store. The count is read from the rules, so it moves to the pull
    request that builds the rules, and §636 has what it counts. Built, and held by
    `DatabaseProfilesTests` and `DatabaseNodesTests`: the count reads the store, and walks
    neither the recycle bin nor the plugin's store.
26. **The store asked again where a client writes into it.** A pairing, a renewal and a client's
    own revocation are judged against the store where the connection arrives, and a rule governs
    none of them, so the router does not ask the store again for them; a request that waits for
    its dialog met a store that came to hold the gate back meanwhile only at its next request. The
    writes that save are built (§627), and each of the three now reads the store again in the
    turn of its write, writes nothing into a store that holds the gate back there, and answers
    the store's word, with a pairing's or a renewal's approval let go and the invitation
    unspent, and `undecided` where a second authority stands by then beside its own. Run over
    stand-ins for each word (`PairingHandlerTests`, `RenewalHandlerTests`,
    `RevocationOperationTests`) and through the writer over real databases for each word,
    `undecided` among them (`PairingInRealDatabaseTests`, `RenewalInRealDatabaseTests`,
    `GiveUpInRealDatabaseTests`). What stays owed is the same in a real KeePass.
27. **What the tab shows past the ceiling on store keys** (§649). A store past it is split with
    nothing judged, and the dialog that keeps a store has nothing to offer, since no keep record
    is read: the tab states the number of keys found and the number of entries hidden, and that
    the entries that brought the keys are to be deleted first. Owed by the pull request that
    builds the tab's listing of the store.
28. **Copies that keep different authorities** (§614), of which nothing was run with the store key
    in each retirement: each copy keeping another authority, then brought together by Synchronize,
    by each import method that keeps identifiers and by KeePassXC, read before and after a Create.
    Run by the pull request that keeps the authority in the store, over real databases brought
    together by `PwDatabase.MergeIn` under Synchronize, Overwrite if newer, Overwrite existing and
    Create new IDs (`AuthorityMergeTests`, `StoreWritesInRealDatabaseTests`): every authority
    retired and read as retired, the store key held by the retirements, nothing served, and a Create
    taken under that key. KeePassXC stays owed. *Could overturn*: what keeping an authority writes
    (S11).
29. **A string's protection taken off and set back** (§628), of which nothing was run: the
    protection of an authority's content taken off in KeePass's entry dialog, the plugin's next
    write, and each merge with a copy that still holds the unprotected version. The pull request
    that keeps the authority in the store runs the content of a real database's authority with its
    protection taken off, protected again at the next write (`StoreWritesInRealDatabaseTests`), and
    the entry dialog's copy put back over that write, which takes the protection off again,
    reproduced with KeePass's types (`StoreWritesUnderDialogsTests`), from which a write is refused
    while an entry dialog holds the entry (§626). KeePass's window and each merge stay owed. *Could
    overturn*: the answer to S18.
30. **The operator's write behind the plugin's own save.** This item held that the settings
    dialog's OK, pressed while a save of the plugin's own pumps under the dialog, as a
    pairing's does, is refused by the writer's guard, with nothing the tab held for the store
    written while the dialog closes as accepted. It was this document's own, read from the code
    and not run, and the run does not bear it out. Measured: a modal dialog's close is raised
    by the dialog's own loop and never by a pump running under it; a press on OK inside the
    pump is handled there, and the close comes once the pump has returned. So the tab's OK
    meets no save of the plugin's own, the write it makes comes after that save, and nothing
    has to be built for it (§627). Run over a plain modal form
    (`ModalDialogCloseTests.A_modal_dialog_s_close_is_raised_only_once_a_pump_under_it_has_returned`)
    and over the real writer and a real database, with the tab on a plain modal form, since
    KeePass's settings dialog cannot be shown outside a running KeePass
    (`TabOkBesideOwnSaveTests.The_tab_s_OK_pressed_inside_a_pairing_s_save_is_made_once_that_save_has_returned`):
    a pairing's save presses OK and pumps, and the revocation the tab held is written, with its
    audit line, once that save has returned, beside the pairing's admission. The same in a
    real KeePass stays owed by the end to end tier.
31. **The longest item a summary entry carries, against the room.** The room holds the content of
    the summary entry a write would write to the ceiling for a string, and not the items that entry
    carries, each a string of its own, and an item is longer than the record it restates: the item
    of a record of kind `version`, a rule's version, is 14 characters longer than that record's
    content, and a keep's item 11 (this document's own, computed from the members each carries and
    not run). A record of kind `version` whose content is 262,131 to 262,144 characters is written,
    since it fits, and from then on every write is refused for the ceiling while the store is
    served, against §627. No record of kind `version` is written yet. Owed by the pull request that
    builds the rules: the room holds the longest item it would carry to the ceiling, or a rule's
    version is composed only where its item fits. The second is taken, and the room stays as it
    is (§627); it is not run until the write that composes a version exists. An item `removed`
    holds a name of 64 characters at most and an identifier, far below the ceiling, so the names
    owe nothing here.
32. **A name added in a copy and removed in another** (§642 row 23), measured in the model alone:
    over real databases the product wrote, brought together by `PwDatabase.MergeIn` under each
    method, in each direction and whatever became of the entries' times; and the same merged by
    KeePassXC, between two KeePass instances with the plugin, and on a device. *Run* over real
    databases the product wrote, merged by `PwDatabase.MergeIn` under each of KeePass's methods in
    each direction, by `StoreProfileMergeTests`: a name added in each copy apart is on the list;
    a name both copies hold, removed in a copy, is off it; removed in each copy and added again in
    a copy, it is off it until added again after the copies met, when the addition names both
    removals and it is on it; a pairing's new name arrives on the list, and a pairing that put a
    removed name back is overruled by a removal in the other copy; a removal deleted for good
    still takes the name off by the summary entry that restates it; twenty names added in each
    copy are all on the list; a name removed in a copy stays off with the removal's entry dated
    back a year and the other copy's addition stamped a day ahead; an addition deleted by hand
    takes the name off wherever its deletion record is, which only a Synchronize carries, so a
    copy merged into by another method keeps the name (row 24); an addition duplicated and
    deduplicated by KeePass's Delete Duplicate Entries, which kept the duplicate under another
    identifier, takes the name off as the deletion of the entry written as the addition does
    (row 24); and a removal both copies hold, edited in a copy past the history limit, stops the
    store wherever the edited entry is kept, which is every method but OverwriteExisting into
    that copy and KeepExisting into the other, and the name stays off where the version written
    is kept (§622). Under Create new IDs each copy names what it named before. The next write of
    the plugin's own leaves every answer as it was and no addition a removal defeated. And the
    model's sequence of names is played through the product's acts and held to the model's file
    by shape, `StoreWriteShapeTests`. *Still owed*: KeePassXC's merge, two KeePass instances
    with the plugin, and a device.
33. **What would take away for good what each renewal costs** (§643; this document's own,
    neither designed nor run). Two things a renewal leaves grow for good: its deletion record,
    which KeePass keeps and every hold passes over, and its name in the `after` of the version
    written anew, which every OK above that version reads, checks, composes and writes again,
    in the version and in its item. The constant of each is taken down (§643), and the line
    stays. **The deletion records** cost a hold nothing more only where the reading keeps, between
    two holds, what it worked out of them: an index of the reading, kept by the very objects of
    KeePass's list of deletion records and of the lists that answer each, as the checked forms
    are kept by their strings (§620), so that a hold passes over only what changed. Kept so, it
    answers nothing the database does not hold, and it is still an answer kept from an earlier
    state in everything but its key, which §620 rules out as worded: it needs the argument the
    forms' cache had, and a hold still looks at every deletion record to know that nothing
    changed, at a few nanoseconds each. **The lists** stop growing only where a version names
    less than every version it replaces: a renewal naming the version it renews and a digest of
    what that version named, or its predecessor alone, with the reading answering a deletion
    record by following the chain of versions or by the digest instead of by a search of a list.
    Neither answers for a version lost from the chain or a digest made apart in another copy,
    which a list answers for today (§617, §639): either changes what a merge of two copies is
    judged by, and owes the runs of item 24 again before it is taken. A compaction (§643) starts
    the lists afresh and leaves the deletion records in the file; a store started again under a
    new key (§632) leaves them as well, and a hold passes over them by their tags alone. Owed by
    the pull request that designs the compaction, before the store reaches the line's five
    seconds.
34. **The standing answers to a renewal** (§618). Run in the model and on real files before
    anything of them was built, `papers/ground/measured-renewal-answers.md` on
    `spikes/storage-survival`: the object, the texts, the meet over the chain of answers at
    every position of a clock, lost versions in six ways, and the sequences A1 to A14, with no
    history below its ideal outside the classes §639 accepts. The reading and the acts are held to
    the vectors it printed (`RenewalTextTests`, `StoredRenewalsTests`, `StoreInForceTests`,
    `RenewalActsTests`, `StoreWriteShapeTests`), and answers given apart in real databases to
    every method of KeePass's merge (`StoreRenewalMergeTests`). Not run: a window, a phone and
    the slow key derivation; KeePassXC releases other than 2.8.0 in the random part on files;
    and the write vectors' sequences over two real databases, which the unit tier plays over the
    store in memory. The sequence A7 is played past Seal again, which is built (§625), and the
    model played it past the seal to the sequence already in the vectors. *Could overturn*:
    nothing of the reading, which the vectors hold.

---

## 14. No migration §645

**Any 0.x release may break an earlier 0.x release** (decision 17). **The move from 0.2 to 0.3
is made by hand** (a later decision): no code in 0.3.0 reads, detects, converts or removes what
0.2 left in a file. The operator starts from scratch under 0.3, and clears what 0.2 left
afterwards.

**The rule this rests on**, which §652 cites as well. While the version begins with 0, a release
owes an earlier release nothing: no migration, no cleanup, no detection, and no reading of what
the earlier release stored. Only a release from 1.0.0 on is owed any of that by a later release.
`API.md` §411 holds the same for the API's contract: a 0.x release may break a client of an
earlier 0.x release.

**The steps, in this order.** The order is part of the decision. The exact path through
KeePass's windows for the last step is verified in a real KeePass by the pull request that
writes the warning.

1. **Under 0.2, delete the authority on the KPG tab, and synchronize every copy.** It is first,
   and it is 0.2's own Delete, because that Delete writes a retirement, and a retirement travels
   with every merge and is removed by none. After the synchronize no copy holds an authority
   that a 0.2 plugin would serve. Removing the authority by hand in a single copy does not do
   that: a KeePass merge never removes plugin data, so what was deleted by hand in a copy comes
   back from another.
2. **Install 0.3 on every machine that serves the database**, before a store is made, so that
   no 0.2 plugin is left to open a database that holds the store (§654).
3. **Start from scratch under 0.3.** Create an authority on the KPG tab, add the profile names,
   pair every client again, each on an invitation and an approval, and enter every rule again on
   each group and each entry that carried a rule. A rule that narrowed a wider rule above it
   has to be entered again with it, or the node is served as the wider rule gives. Nothing of a
   revocation has to be carried over: every certificate that 0.2 issued chains to an authority
   that 0.3 does not read, and is refused (reasoning).
4. **Clear what 0.2 left, in each copy.** In each, because a merge never removes plugin data: a
   copy that still holds it hands it back to the others at the next synchronize.

**And grant on groups, not at the root.** A machine that was forgotten in step 2 serves the
store's group by the rule of the root, and with no rule at the root it serves nothing of it
(§654).

**Where the operator reads this.** In the release notes of 0.3.0, as a breaking item, and in
`docs/`, as a short section "Moving from 0.2" on the getting-started page (§654). The breaking
item is written with the code (below). The section in `docs/`, with the steps in this order,
is written by the pull request that writes the operator's documentation of the store, once
every part of the store is built. Until then a build still reads the rules and the standing
answers to a renewal where they were kept before, so the steps would not hold of that build, and
the path of the last step can be verified only against a plugin that reads nothing of what is
cleared (§644).

**Two client programs have to change for 0.3.0**, which a 0.x minor release may ask (`API.md`
§411). **A program that named fields of its own with the prefix `KPG.`**: such a field is out of
every route from 0.3.0 on (§633), so such a program renames those fields before the upgrade. **And
a program that reads the identity route through the 0.2 client**: with the names in the store,
a list of profiles that will not read is no state, since a damaged record of a name stops the
store (§621, §624), so `GET /gate/whoami` answers no `profilesUnreadable`. The 0.2 client
refuses an identity without that member, so its `identity()` and `whoami` fail against a 0.3
gate until the client is updated. The release notes mark both as breaking, as `API.md` §411
records. `API.md` §411 has a change break a client when a program that worked before has to
change. Pairing again is the operator's act and the client's ordinary command. A word that §656
adds is answered under `503`, and a 0.2 client takes a word it does not know for the gate being
unavailable (read from the client). So a 0.2 client works with a 0.3 gate on every route but the
identity route. §411 also counts what a
certificate or an admission record carries: the admission changes where it is stored and what
it names (S4, S29), and that holds only while nothing of it changes what a client is sent. The
admission is built that way (§615): the bundle that a pairing and a renewal answer with is as it
was, a certificate carries the claims it carried, and `API.md` §411 records it.

**`CHANGELOG.md` gains its entry with the code.** The pull request that makes a 0.2 database
start over for somebody running the plugin writes that entry, marked **Breaking**, and this
document changes nothing for a person running 0.2.0. That is the pull request that keeps the
authority in the store, after which no authority that 0.2 made is served, and every client
pairs again. Each pull request after it that moves a further part into the store adds to the
same entry what has to be entered again.

**What may refer to the store of 0.2.** Nothing merged for 0.3.0 refers to how the store of 0.2
worked: not code, comments, tests, the routing document, the client, text an operator reads, the
log, the audit or `docs/`. The design documents, `CHANGELOG.md` and `CLAUDE.md` are the
exception, where a reversal is recorded with its argument. A warning that names version 0.2 and
the steps of the move is no such reference: it tells an operator what to do, and nothing of how
0.2 kept its data. The code that exists only for the store of 0.2 is removed, with its tests,
by the pull request that replaces it.

---

## 15. To settle in the implementing pull request §646

Nothing in the three groups of this section is decided. Each item is a format, a text or a
behavior that no decision, no measured code and no sentence of the design paper fixes, or on
which those sources differ. An option is a proposal, and what follows "has to satisfy" is the
constraint the decisions put on any answer. An item keeps its number for good, so that a
reference to it still leads somewhere.

The items are in three groups: what the first pull request that builds the store has to settle,
because a record's form is final from 0.3.0 on; what a later pull request has to settle; and
what is the project's to decide and no pull request's. A list before them names what was
settled since, and where an item was settled in part, what is left of it stands in its group
under the same number.

**Settled, and where the answer is.** An item that the project decided names the decision's
section. An item marked **fixed for the core** was left to the pull request that builds the
store's core and is fixed there, as the implementer's detail and not as a decision of the
project; its section has the reason, what it rules out and the test that holds it. An item
marked **fixed for the first writes** was left to the pull requests that keep the authority
and the admissions in the store, and is fixed in the same manner. So is an item marked **fixed
for the rules**, which was left to the pull request that keeps the rules in the store.

- **S1. The marker `v`.** Fixed for the core: no record holds such a member, and a reader
  passes over it where it is present, §607.
- **S2. Where the random bytes come from.** The store key, the nonce and the second half of the
  group's identifier are drawn from the generator the product draws the gate key from today
  (read from the product).
- **S3. The authority's content.** Fixed for the first writes: a member for each part of
  today's document beside `id` and `storeKey`, `id` the digest of the authority's certificate,
  and an authority that passes its check and is not whole damaged, never served and retired
  like any other, §614.
- **S4. The admission's content.** Fixed for the first writes: a member for each field of
  today's record, `profiles` in ordinal order and `admitted` in a single form, `issuer` held to
  the anchor pin, a member the table does not name passed over, and a record that does not read
  admitting nobody and counting as no admission anywhere, §615; a pairing's and a renewal's
  write read back by its own entries, an approval held for a renewal held to the entry of the
  admission it renews, and an approval let go where its write meets the store's word, §627.
- **S5. The revocation's content.** Fixed for the first writes: a single revocation, holding
  the client key alone, §615; and the records of kind `client` a write deletes, told by the
  texts the core keeps, §627.
- **S6. A certificate given up by its own client.** A record of a kind of its own, §648.
- **S8. The form of a rule's version and of the switch's.** Fixed for the rules: the object and the
  text of each, read strictly, with a text out of form refusing, §617; the identifier of a rule's
  version, with a tag of the profile's name in bytes 9 and 10, and the switch's as run, §609 and
  §658; what a deletion record tells of each, §617; and which deletion records a subject is hashed
  for, §620. The identifier is the form this item proposed from a measurement, with the measurement
  in §609; two names whose tags meet were run since, and the form is supported with a reading that
  tells of every name of a tag, §609. A version that lets a profile through holds what arrived, in
  its text, §617. The conditions this item set hold of the form: an empty rule and a rule taken off
  are told apart in the text; a profile's name in an object is read as the list of profile names
  reads it; a deletion record tells which node's and which profile's version it names by a hash for
  each node and a tag for each name, and not by every node with every name; and the form is fixed
  with the first format number, §650.
- **S9, in part. How the grant editor shows a rule in several versions, and what removing a
  version writes.** As rows bound together, with no message box and no dialog held at OK,
  §657. Removing a version writes again each version that is left, fixed as measured, §617.
  Fixed for the rules: the limit the resolver carries up its walk, §617; and what a row or a
  switch that refuses shows, with setting it replacing it, §657. A row of a group offers its X
  and no list of rules, and a switch in versions that differ shows a third mark that counts as
  ticked, §657. Every row has an X that takes the rule off, so how the table shows a row that
  cannot be taken off is an open point no more, §617 and §657; and a profile that a node cuts
  shows a limit row whose X settles it, §657. What is left is below.
- **S10. A record of the recycle bin.** Not in 0.3.0, §619.
- **S11. Several groups, and keeping an authority.** Fixed for the first writes: a retirement
  for each authority not kept, each holding the store key, §614; and the group, outside the
  recycle bin, that holds the most records under the store key, or else a group of the
  plugin's making, §606.
- **S12. The height.** Fixed for the core: two bytes, from 1 to 65,535, and a write whose
  height would be higher writes 65,535; no write is refused for it, §609.
- **S13, in part. The reader's ceilings against the store's growth.** Fixed for the core: what
  a reader refuses of a version, whole, §607; the plugin's own ceilings below the reader's, and
  the operator told from half of either, §643. What was left is S35, in this list.
- **S14. The texts a person reads in KeePass's window.** Fixed for the first writes: the
  measured texts, and a mark in the title and in the notes of each entry that carries the
  store key, §607.
- **S15. Where a rule waits between the dialog's OK and the save, and what is added at the
  OK.** In the database, in memory, added at the OK, §629. Fixed for the rules: the addition is
  a write of the plugin's own, whole, which sets edited records back and goes through the
  writer's guard as the dialog closes, and what the editor's buffer holds for it, §629; what it
  asks of KeePass's windows, §626; and a version composed only where its item fits, §627. Where
  that write is refused, the log names it and a message is shown once the dialog has closed,
  §629. The condition this item set on a rule narrowed while a save runs rests on row 15 of
  §639; it was run for a rule set inside a save of the plugin's own, §644 item 6, and a narrowing
  stays owed, §644 item 11.
- **S16. Which word a client gets while the gate holds back, and to which requests.** A word for
  each reason, and the five words of §656. Fixed for the core: the operations that `frozen` is
  answered to, and the order of the words where several states hold, §656.
- **S17. Where the guard's setting lives.** The plugin's window under Tools, §635.
- **S18. Whether a string is protected.** Fixed for the first writes: a string whose protection
  was taken off is protected again at the plugin's next own write, as an edited record is set
  back, §628.
- **S19. What an expired certificate left.** It stays until the compaction, and nothing is
  removed by a clock, §643.
- **S20. What the reading costs, and what may be kept between requests.** Fixed for the core:
  the checked form of a version, kept by the string objects it holds, and nothing else, with an
  estimate of the cost, §620. The measurement through the product stays owed, §644.
- **S21. A ceiling on profile names, and on the profiles ruled at a node.** No ceiling is kept
  for either: not on the list of names, §616, and not on the profiles ruled at a node, §617.
- **S22. What the operator's documentation advises for what 0.2 left in the file.** The steps
  of §645, in the release notes and in `docs/`.
- **S23, in part. The form of the kinds that were in no run.** Fixed for the core: the kinds
  `withdrawn`, `kept` and `format`, with what the core reads of every kind, §658; the keep
  record, §649; the version record, for good, and the first format number, §650; what a
  deletion record tells of each, §623. A rule and a switch are records of kind `version`, as
  measured, with an object of a type the core knows, §658. Fixed for the first writes: the
  members of `client` its meaning reads, held by the core as texts where present, §658. What is
  left of S7 is fixed for the answers, in this list; S8 is fixed for the rules and S29 for the
  first writes, in this list.
- **S24. Two admissions that name the same certificate.** Fixed for the first writes: a
  certificate that several admissions in force name is admitted by none of them, an admission
  that a decision refuses and a record that does not read counted among none of them, and a
  renewal from such a certificate not counting where that certificate was not taken away, §615.
- **S25. An entry of the plugin's stripped of all its fields.** A deleted record, and not
  damage, §621.
- **S26, in part. What is left of the version record.** Fixed for the core: a version record is
  written where the plugin's version rises, §650; the form of the description the test holds,
  §650; a reader passes over a member it does not know, §607. The caption of the button that
  raises a store and the words of its warning are left to the release that first moves the
  format number, since no state of 0.3.0 offers the button, §652.
- **S27. A 0.2 plugin on a database that holds the store.** A warning and no code, §654.
- **S28, in part. A command that starts the store again.** A command on the tab, §632. Fixed
  for the first writes: a store that Create starts gets a group of the plugin's own, also
  where the group of an earlier store is still in the tree, §606 and §632. What is left is
  below.
- **S29. A renewal from a certificate that was taken away.** Fixed for the first writes: the
  members `from` and `renewed`, an item for each record with that record's identifier, a
  renewal counting only where every statement that took its certificate of origin away names
  it, the chain followed through the records that took certificates away, and an admission
  whose certificate of origin is neither admitted nor taken away not counting, §648; the
  renewals a record names, a record for each certificate of origin where several admissions of
  the certificate taken away hold other origins, and the chain asking both the statements and
  the admission of a certificate taken away while that admission is still in force, §648.
- **S31. How many bytes of the check the identifier holds.** Fixed for the core: six, as
  measured, with what that does and does not bind, §609.
- **S32. What the grant editor writes while a rule still shows several rows.** A rule is a
  record for each node and profile, so a row is a record and nothing else at the node is held
  back, §617 and §657.
- **S33. Whether every word of §656 can be sent.** Fixed for the core: a word is answered under
  each authority that stands, with its own host certificate; where the reading finds none, from
  what the gate held when it last served the database; and otherwise the connection is
  dropped, §656. Every word can reach a client, and `lost` only from a gate that was serving
  when the key went.
- **S34. Rules changed apart that add up to more than either copy's table gave.** A version
  that lets a profile through records what arrived, and the node gives what arrives cut to
  it, §617; a write above writes anew the versions below that were following, §617 and §629.
  At a single node under a switch that refuses, the X takes the rule off as `off:deny`, which
  reverses the earlier answer under this item, by which removing a row there wrote an empty
  rule and removing it again wrote nothing, §617 and §657. The sequences closed, with the runs,
  are in §639. Accepted with it, each in §639: a narrowing above that reaches a copy in a
  single direction and is widened again apart, within what the node's own version recorded;
  two rules taken off apart, the upper of which gave what arrived anyway, followed by a
  widening after the copies met; a limit that no longer cuts and is passed by a later
  widening in a copy that holds it; a node with no rule moved apart from a change above; and
  a name put back on the list apart from a row widened for it. Its cost is a node that is cut
  until its limit is removed, §642. The conditions this item set hold: two copies that change
  different profiles at a node do not meet, and an empty rule and a rule taken off stay apart
  for the operator, the X making the second and the operator choosing the first.
- **S35. A store that has no room for its next write.** The gate serves nothing for that
  database, answered `stopped`, until the store is started again, and the answer is looked at
  again once a compaction exists, §643.
- **S7. The answer to a renewal.** Fixed for the answers, §618: the object, `renew:` and the
  client key, with any other object of type `renew` the object of nothing; the texts
  `refuse:forever`, `refuse:until:` and a moment as the wire writes it, and `ask`, each read
  strictly, with a text out of form refusing forever; the strictest standing version as the
  answer, the later of two moments the stricter as texts, and a lost version refusing forever;
  what a deletion record tells of an answer, by the client keys the file knows; a clock read
  only where a renewal is answered and where the clients page draws a row; and what a version
  names, which is what its writer was shown, a lift with nothing left to name writing nothing.
  Measured before it was built, `papers/ground/measured-renewal-answers.md` on
  `spikes/storage-survival`. Fixed for the answers, §627 and `PAIRING.md` §312: an answer given
  on the renewal dialog is written in a turn of the writer, saved before the client is answered,
  as an approved renewal is, and believed on the flag and the file, with an answer whose write
  is refused or whose save does not land held for this session and told at once, and nothing
  left for a later save; a lift is an act of the settings dialog's buffer. Built in the pull
  request that keeps the answers in the store, §618 "What is built". It satisfies: two answers
  made apart give the stricter; an answer for this session has no record; no merge decides by a
  time. The answers to the three questions of that pull request, an answer about the client, an
  answer saved as Approve's record is, and the refusal under Database Maintenance kept, are later
  decisions (§626).

**What the first pull request that builds the store settles.** Every item of this group is
fixed, and the list above names where: S1, S12, S13, S16, S20, S23, S26, S31 and S33.

**What a later pull request settles.**

- **S9. What the resolver and the editor need for versions made apart** (§617, §657). Decided:
  what all versions allow, and what the operator sees. By reasoning: versions that grant give
  the letters all of them hold; the switch in versions made apart refuses. Fixed as measured:
  removing a row of a group writes again each version that is left (§617). Fixed for the rules:
  the limit the resolver carries up its walk where a version that grants stands beside a
  version that takes the rule off (§617). This item held open how the table shows a row that
  is an empty rule under a switch that refuses, which a further removal did not take off; a
  later decision has every X take the rule off, and that point is gone (§657). Not fixed: the
  drawing where the prototype left it weak: a bold dot that is faint, bold letters with less
  contrast on the red, a table of four rows, and a high contrast setting. Has to satisfy: the
  result is never wider than any of the versions, for every letter; a switch of a rule without
  a verb still grants nothing; what tells the rows apart does not rest on color alone; and a
  removed row stops standing in every copy the write reaches. The form fixed meets the last
  condition except where two copies each remove a different version apart: those versions
  then stand again, which is narrower, and no form that was run meets the condition there
  without ending wider (§617).
- **S28. The command that starts the store again, in its texts and its place** (§632). Decided:
  the command, its warning, a store under a new key, every other key set aside. Proposed: what
  it writes. Not fixed: its caption, the words of its warning, and on which page of the tab and
  in which states it is offered. Has to satisfy: a plugin that the store's format holds back, or
  that does not read the store's form, can use it; nothing of the earlier store is converted;
  and the earlier store coming back by a merge does not hold the plugin back again.
- **S30. The entries under a store key that was not kept** (§649). They stay in the file, passed
  over and shown, an authority's private key among them. Not fixed: whether anything ever
  removes them. Has to satisfy: no write removes them that a different answer in another copy
  would turn into the loss of both stores, as a deletion with deletion records does under
  Synchronize.

**Decisions for the project.** The questions this group held are answered, and the list above
names where. The last of them was S34, whether rules changed apart at two nodes may add up to
more than either copy's table gave, with accepting it as the answer named until then: it is
answered by the record of what arrived, and nothing stands in this group.
