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
> Release 0.2.0 ships a store in the database's own custom data, with a `KPG` key in the custom
> data of groups and entries, and what of it this build still keeps is in `CA.md` §204. What is
> built of this store: the API's guard of §633, with what tells a node of the store by its
> identifier; the reading of §620 to §624, with what is in force under the store key, the room a
> store has for its next write (§627, §643), and the words `split`, `outdated` and `stopped` of
> §656; and the authority and its retirements of §614, written by the plugin's own writes of
> §627 with the records set back of §628, and served from what stands. §614, §620, §627, §628,
> §633, §643 and §656 state which parts those are, and nothing else below describes what the
> plugin does today. Each pull request that builds a part of this store brings `CA.md`,
> `DESIGN.md`, `PAIRING.md`, `API.md` and `docs/` into line with the code it adds. Until then
> those documents hold for 0.2.0 where they describe a part not built, and this document holds
> for what replaces its store.

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
  §644 has what the run showed and what of it stays owed before the rules are built.
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
| The authority takes the file to KDBX 4.1, for the time each custom data key carries | KDBX 4.0, nothing above it | no time decides anything, and nothing the store uses needs 4.1 |
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
counting.

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
                 version record (§650), and 0 for every other kind
bytes 11 to 16   the first 6 bytes of the check
```

| Kind | Subject |
| --- | --- |
| `authority` | `authority:` and the authority's `id` |
| `retired` | `authority:` and the retired authority's `id` |
| `client`, `revoked` | `client:` and the client key |
| `profile`, `removed` | `profile:` and the name |
| `version` | the object it is a version of. As measured: `rule:` and the node's identifier as 32 hexadecimal characters, or `renew:` and the client key. A rule's object is now a node and a profile, and the switch's a node (§617), in a form that is S8 |
| `head` | `store` |
| `withdrawn` | `certificate:` and the admission key of the certificate taken away (S23) |
| `kept` | `key:` and the name of the store key not kept, §649 (S23) |
| `format` | `format:` and the format number in decimal (S23) |

§658 has every kind with what the core reads of it.

**An entry is a record only under the identifier its content gives.** The reading computes the
check from the entry's strings, then the identifier from the kind, the subject, the height and
that check, and compares it with the entry's own. A copy made by Duplicate Entry, Duplicate Group,
copy and paste, or an import with Create new IDs holds the strings under another identifier,
fails that comparison, and is passed over (§621).

**Every version of an entry is compared with the identifier of the entry that holds it** (as
measured): the current strings and each item of the history alike. An identifier that a history
item carries for itself is never read.

**The first eight bytes are for deletion records.** A deletion record is an identifier and a time
and nothing more. With this layout the deletion record of a record still tells a gate that holds
the key what kind of record it was and what it was about, and for a summary entry how high
(§623). The tag and the subject hash are keyed, so they mean nothing to anybody without the key,
and a file made outside cannot carry a deletion record that names a record of this store (the
design paper 3.2, 3.3).

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
deletion record has to tell (§623), and the form of a rule's version may come to use them
(S8). So the layout is the measured layout, for every kind, and every count of this document is
of it. *Held by* `StoreFormatTests`, against the measured code's own identifiers, and by
`StoreReadingTests`: an entry whose identifier differs from the identifier its content gives in
any single byte is no record.

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
| `{"kind":"retired",...}` | `authority` | a retirement of that authority |
| `{"kind":"removed",...}` | `name`, `id` | the removal of a profile name, with the identifier of the removal's record |
| `{"kind":"version",...}` | `id`, `object`, `after`, `doc` | a standing version of a rule, of a node's switch or of an answer to a renewal, whole, with its text. With a rule for each node and profile there is an item for each profile's standing version at each node (§617) |
| `{"kind":"withdrawn",...}` | `id`, `certificate`, `renewed`, and `from` where the record holds it | a record that took a certificate away, with that record's identifier and what the record holds, member for member (§648) |
| `{"kind":"kept",...}` | `id`, `key`, `after` | a keep record, with its identifier, the name of the key not kept and the keep records it replaces (§649) |
| `{"kind":"format",...}` | `format`, `plugin` | the highest format number and the highest plugin version the store states (§650). Every summary entry holds exactly this single item of the kind |

It restates every revocation, every retirement, every removal and every standing version. It
holds no authority, no admission and no profile addition, because losing any of those admits
nobody. Each alone, the records or the newest summary entry, is enough to compute what takes
access away, and the reading unites them (§623).

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
  is 4.1, `CA.md` §204.

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
| `retired` | `authority:` and `authority` | `authority`, and `storeKey` where it holds the key | counts | an item `retired` with the same `authority` | nothing |
| `client` | `client:` and `client` | `client` | out of force | nothing | nothing: §615, §648 |
| `revoked` | `client:` and `client` | `client` | counts | an item `revoked` with the same `client` | nothing: §615 |
| `withdrawn` | `certificate:` and `certificate` | `certificate`, and `renewed` and `from` where it holds them | counts | an item `withdrawn` whose `id` is the record's identifier, with the same `certificate`, `renewed` and `from` | nothing: §648 |
| `profile` | `profile:` and `name` | `name`, `after` | out of force | nothing | S21 |
| `removed` | `profile:` and `name` | `name` | counts | an item `removed` with the same `name`, whose `id` is the record's identifier | nothing |
| `version` | the `object` | `object`, `after`, `doc` | out of force | an item `version` whose `id` is the record's identifier, where no version in force names the record in `after` | S7, S8 |
| `kept` | `key:` and `key` | `key`, `after` | counts | an item `kept` whose `id` is the record's identifier | nothing |
| `format` | `format:` and `format` | `format`, `plugin` | counts | the item `format` of a summary entry that states a format number at least the record's | nothing |
| `head` | `store` | `height`, `after`, and every item | out of force | nothing | nothing |

**The forms of the members.** `id`, `authority`, `client`, `name`, `object`, `doc`, `renewed`
and `from` are texts the core compares and does not look into. `storeKey` and `certificate` are
64 lowercase hexadecimal characters, and `key` is the name of a store key (§649). `after` is a
list of identifiers (§607). `format` and `height` are whole numbers from 1 to 65,535 in decimal,
with no sign and no leading zero, and `plugin` is three such numbers, each from 0, joined by
periods.

**The members a kind's meaning reads** are kept with the checked form as the members above are
(§620), and none of them decides whether a version reads as its kind. Each is read where it is
present, and what a content that lacks such a member, or holds it out of form, comes to is
decided by the kind's meaning and never by a stop. They are, for `client`: `certificate`,
`der`, `issuer`, `profiles`, `subject`, `admitted` and `from` (§615, §648). The `renewed` and
the `from` of `withdrawn` and of its item are read by the core where they are present, since an
item restates a record only with the same of each (§648); a record or an item that lacks
either still reads as its kind, and what each means is decided there. An authority's document
is not among them: it holds private keys, so it is read from the version's own content where a
gate needs it, and is kept nowhere as text (§614, §632).

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

**What is left to a later pull request is what a kind means, and never whether it is read.**
The text of an object after its type and the text of `doc` are fixed by the pull requests that
build those kinds. The members of an authority and of an admission beside those named here,
and the renewals a `withdrawn` record names, are fixed in §614, §615 and §648. A member that a
kind's meaning adds is passed over by the core (§607). Where the form of a rule's version takes
bytes 9 and 10 of its identifier (S8), the pull request that builds the rules changes the row
of `version` here.

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
each act held to the authorities that stood when it was composed. The admissions and the
revocations are still kept where `CA.md` §204 keeps them, and are handed the authorities that
stand: a client is judged by the anchor pin against the authority served before any record is
asked, so a record of a certificate another authority issued admits nobody, and a revocation
names the standing authority that issued its certificate. Recreate and Delete take every
admission record, revocation and standing answer out of the custom data only where the store
write takes the act: at the tab's OK the store is written first, and the custom data loses them
as it stands then, a record that a pairing landed under the dialog among them. An act refused or
left out at OK takes nothing away, since a revocation taken away while the authority that
issued its certificate is still served lets a stale copy merged in admit that client again. The
admissions' move into the store (§615) makes their removal part of the act. *Held by*
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
| `profiles` | the approved names, separated by single spaces, as measured, and empty for none |
| `subject` | the subject as issued |
| `admitted` | the moment of the approval, as today's record writes it. It is shown, and decides nothing |
| `from` | on an admission that a renewal writes, the admission key of the certificate it was renewed from (§648). A pairing's admission holds no such member |

A member for each field, and not a single member holding today's text: a content is a flat
object of strings already, and a text of lines inside a string would be a second grammar for
the same fields.

**An admission reads** where every member but `from` is present and in its form, `from` is in
its form where it is present, `der` decodes to a bare certificate whose digest is
`certificate`, and the names in `profiles` are read as today, a name that could not have been
issued as a claim being dropped, `CA.md` §223. **A record of kind `client` that passes its
check and does not read admits nobody**, which is the answer for no record at all, and the
clients page names it by its entry. The reader stays total: nothing a content holds makes it
throw. **`issuer` is held to the anchor pin.** The gate judges a certificate against the
authority it serves by the pin, `CA.md` §214, before any record is looked up, and admits it
only where its record's `issuer` is that authority's `id`, so a record whose `issuer` disagrees
with the pin admits nobody. Nothing of this changes what a client is sent, `API.md` §411: the
bundle of a pairing and of a renewal is as it was. *Held by* `StoreAdmissionsTests`: a record
that lacks each member in turn, holds each out of form, or holds a certificate whose digest is
not `certificate`, admits nobody and stops nothing; a record whose `issuer` is another
authority's admits nobody under the authority served; and by `StoreWriteTests` against the
measured code for the content's text, its check and its identifier.

**A certificate is admitted** when its record is not beside a deletion record naming it, no
revocation names its client key, it was not taken away and was not renewed from a certificate
taken away (§648), no retirement names its issuer, and its issuer is the authority served. Its
profiles are the record's, less every name the profile list does not hold, as today. The
admission record stays load bearing: a certificate with no record is refused.

**Two admissions that name the same certificate** are not what the plugin writes, and whoever
holds the store key can write them. The measured code let whichever it read last decide the
profiles, so the answer turned on the order of entries. **A certificate that several admissions
in force name is admitted by none of them** (S24). The answer does not turn on the order in
which entries are read, and it is never more than any single admission of them gives. It rules
out the profiles all of them hold, under which a record that somebody added beside the
plugin's own would still leave the certificate admitted. The clients page shows such a
certificate with every record that names it, and the operator takes the certificate away or
revokes the client. *Held by* `StoreAdmissionsTests`: several admissions for a certificate,
read in every order, admit nobody, and the same admissions with a deletion record naming all
but a single admission admit by that admission.

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
at the plugin's next write (§627), and admits nobody before that. An admission lost in any way
fails closed: that client pairs again.

**A revocation takes a client away whole.** It is the operator's, made on the clients page. A
single certificate is taken away by a record of another kind, by its own client or by the
operator (§648). A record that a lost answer to a renewal left behind is cleared that way by
the operator: the client never held that certificate and cannot give it up, `PAIRING.md` §312.

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
`certificate`. In the member `renewed` it names the renewals made from that certificate that
its writer had read (below): the admission key of each renewed certificate, each once, in
ordinal order, separated by single spaces, and empty for none. In the member `from` it holds
the admission key of the
certificate that the certificate taken away was itself renewed from, where its admission named
such a certificate, and it holds no such member otherwise. The subject in its identifier is
`certificate:` and the admission key of the certificate taken away, so that a deletion record
of it still tells which certificate was taken away (§609).

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
2. **A record that takes a certificate away names every admission renewed from that certificate
   that its writer had read**, as a record names in `after` what its writer had read (§610):
   in `renewed`, the certificate of each such admission in force. Those are the renewals made
   before the certificate was taken away, in the copy where it was taken away, the certificate
   the client has just renewed to among them.
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
   each of them has to pass. A certificate met a second time along its own chain does not
   count.
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
certificate that works. Only the operator stops that, by taking that certificate away as well or
by revoking the client. The certificate a client has just renewed to is never refused by the
old certificate being given up at a gate whose copy holds the renewal, since the record
written there names it. What refuses it is a further statement about the old certificate,
made at a gate that had not read the renewal, which is the sequence the third rule refuses.
No answer turns on the order in which entries are read, since what every statement names is
the same in every order. *Held by* `StoreAdmissionsTests`: a renewal that the record names is
admitted and a renewal it does not name is not; a renewal onward from a renewal that does not
count is not admitted, also once the certificate between was given up; a renewal from a
certificate that only a deletion record tells of is not admitted; an admission whose
certificate of origin is nowhere is not admitted; records for a certificate that hold
different certificates of origin admit only where each passes; where several records took a
certificate away, a renewal that each of them names is admitted and a renewal that any of them
does not name is not, in every order of entries; a deletion record of a record that an item
restates changes nothing, and a deletion record that neither a record nor an item answers for
refuses every renewal from that certificate; and a chain that leads back into itself admits
nobody.

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
its own certificate and nobody else's, `CA.md` §217. It writes this record where it writes a
revocation today, through the single writer, and answers once the database is saved and read
back. The reference client goes on calling it with the old certificate after a renewal,
`PAIRING.md` §312. A client that gives up the only certificate it holds has none to renew with,
and pairs again as a new client (reasoning).

**The clients page.** It lists a client's certificates as it lists a client's records today, and
offers both: revoking the client, which writes a revocation by its key (§615), and taking a
single certificate away, which writes this record. The single removal is how a record is cleared
that a lost answer to a renewal left behind, `PAIRING.md` §312: the client never held that
certificate and cannot give it up. Like everything on the tab, both wait for the settings
dialog's OK.

**The audit.** As today, `DESIGN.md` §120 and `CA.md` §217: a line by the admission key that was
taken away and the client key its record carried, for the client's own act written by the writer
on its landed path, and for the operator's when the dialog's OK lands it.

**What it has to satisfy.** A certificate its client gives up is refused from the next request.
The certificate a renewal issued keeps working. A lost answer to a renewal costs the database
nothing, `PAIRING.md` §312. Whatever takes a certificate away is restated by the summary entry.
A revocation by the operator still refuses a certificate renewed at another gate.

**Not run.** No run held this kind or the rule about renewals. A gate that has not taken the
file in still admits a certificate taken away at another gate, as it admits a client revoked
there (§639). §644 names the runs owed.

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
`DESIGN.md` §139. Whether the list keeps a ceiling is S21.

### Rules §617

**A rule is stored as a record for each node and profile** (a later decision). Each row of the
grant editor is a record of its own with its own versions: a record of kind `version` whose
object is a node and a profile. The node's switch "Refuse any profile not named here", which is
`default` in today's rule document, is a record of its own with its own versions, whose object
is the node. Nothing is on the node.

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
(`measured-rules-per-profile` 3). §644 has what was run and what stays owed.

**What a version holds** (the forms are S8). Beside `object` and `after` (§610), a version of a
profile's rule holds a single thing of three:

- **the letters granted**, out of the eight of `CRUSDLAP`;
- **no letter**, which is an empty rule: the profile may do nothing from there down, as an empty
  array is today, `DESIGN.md` §136;
- **the rule taken off**, which is the profile not named at that node. It is told apart from an
  empty rule.

A version of the switch holds whether the node refuses a profile it does not name or lets the
walk go on, which is `deny` or `inherit` today.

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

**Today's rule model is unchanged** (decision 9): the nearest rule wins for each profile, profiles
are combined with or, a rule can be empty, the switch answers where no rule names a profile, and
the verbs and the two switches of a rule are `CRUSDLAP`, `DESIGN.md` §136 and §137. What changes
is where a rule is kept, and not what it means. The reading puts together, for a node, the rules
of the profiles named there and the switch, and hands the resolver what today's rule document
hands it (reasoning).

**A profile's rule at a node is the versions of its object that nothing names.** A single
standing version is the rule. A newer version names every version of that object that its
writer had read, whatever it holds: that is the operator changing a row, and it may widen. No
standing version and no trace of any is a profile that the node does not name, which the switch
answers, as today.

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
granting versions hold. What the resolver needs for it is S9.

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

**Versions of the switch made apart refuse while they differ** (reasoning from what all
versions allow). For a profile the node does not name, a switch that refuses allows nothing and
a switch that inherits allows what arrives from above, and what both allow is nothing. It was
run in a single sequence (`measured-rules-per-profile` 7): a switch turned on in a copy, and
turned on and then off in another, left the merged file refusing the profile the node does not
name, as the copy with the switch on did.

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

**Rows changed apart can still add up to more than either copy's table gave, across nodes.** A
profile's rule, a node's switch and the rules of another node are objects of their own, so a
change to each, made apart, holds in full. The decision above answers the case at a single
node under a switch that refuses, which S34 put to the project. The case it does not reach is
in §639, where it is measured in both forms, and S34 holds what is left of the question.

**An object refuses** where the file shows that a version existed and holds its text nowhere: a
deletion record tells of a version of the object that no standing version names as replaced and
no summary entry in force carries, or no version stands and a version is present beside a
deletion record naming it. A profile's rule that refuses grants that profile nothing at the
node and below. A switch that refuses in that way reads as refusing: the node grants nothing to
a profile it does not name. In the whole-rule form the node refused for every profile; with an
object for each profile the refusal is that profile's alone. Measured
(`measured-rules-per-profile` 8): a version of a row, deleted for good with its summary entry
and with deletion records for both, stopped the store; the listing named the lost row alone;
and sealing the store again wrote an empty rule for that row and left the other profile's row
at the same node as it was. A lost version of the switch was written as the switch refusing.
What lets a deletion record tell which profile's rule it was is S8.

**What replaces what.** A version that a present version of the same object names is deleted
with a deletion record at the plugin's next write (§627). Every standing version is restated
whole by the summary entry.

**What a node's place does.** A node under its own rule keeps it wherever it is moved: a node
with a rule granting nothing under a root that grants gave nothing in every cell of 90 where it
exists, in the whole-rule form (`measured-store-as-a-whole` 3.2) and with a record for each
profile alike (`measured-rules-per-profile` 3). A copy of a node has a new identifier, no rule
names it, and it is read under the rule of the place it lands in (§639). A profile whose rule
at a node is lost in every carrier, with no trace, is a profile the node does not name.

### Renewal answers §618

A standing answer to a renewal is a version, as a rule is, of an object of its own. As measured
the object is `renew:` and the client key, and the answer is refused forever when the standing
versions together allow nothing, and asked of the operator otherwise. Two answers made apart give
the stricter, so a lift made without having seen a refusal loses to it. An answer for this
session has no record and stays in the plugin's memory, as today, `PAIRING.md` §312.

The measured code ran forever and allowed, with stand-in documents. Today's answer is about a
certificate and can hold a date, and the design paper names the certificate as well. The object,
the text of each answer and the answer until a date are S7.

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
strings pass the check under a key the file holds, and what they parse to. It is kept by the
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
entry 39 ms. Where every entry holds ten history items, 60, 59 and 100 ms, and the worst run 143
ms. The first reading, with nothing kept, adds about 40 ms once. A request takes two holds that
read the store. A change's turn on KeePass's thread reads it once: 2.6 ms, and 58 ms where every
entry holds ten history items, where it read it at each question and took 6.3 and 120 ms. A
file that brings a store key of its own for each of its entries costs what any file of its size
costs (§649): 2 ms for a second reading of 300 such entries and 1 ms for 1,000, where the work
under each key took 0.7 and 19 seconds. With what is in force and the room worked out at every
reading as well (§623, §627), the same holds measured 2.4, 2.7 and 48 ms, and 59, 57 and 101 ms
where every entry holds ten history items, which is the earlier run within what two runs on one
machine differ by; and a store at both of the plugin's own ceilings (§643), a summary entry
naming 7,900 summary entries each with its deletion record and restating 9,000 revocations, 900
of them with a deletion record besides, held the thread for 21 ms. So the estimate holds in its
shape, the walk somewhat dearer in the product, and reading every entry at every request stays
well within the five seconds a hold is let run, with a full history on every entry the case
that shows. *Held by*
`StoreReadingBoundTests`, `StoreReadingTests` and
`StoreInRealDatabaseTests`: a string replaced by an object of another text is read at the next
hold with the entry's times left as they were; a second question inside a hold walks nothing;
and nothing kept outlasts a lock.

**What is built.** The reading of this section, of §621 to §624, of §649 to §651 and of §658,
whole as far as it decides whether a store may be served: `StoreReading` over the candidates
and the deletion records `StoreSource` walks, read once for each hold through
`IReadHold.Stamp`, with the checked forms kept in `StoreKept` and let go, their keys overwritten,
with the target at a lock and a close. It answers the state of §656 and the guard's keys and
tags (§633). What is in force (§623) is `StoreInForce`, worked out for a store that is whole or
stopped: every record under the store key with whether a deletion record names it and whether
it counts by its history, the summary entries present, in force and told of by deletion records,
what the retirements, the revocations and the removals take away by record, by item and by
deletion record, and the statements that took certificates away by record, by item and by a
deletion record of an admission key a record or an item of that kind names (steps 1, 2, 4 and
7), the highest format number and plugin version (step 6), the authorities that stand, and the
room of §627. A deletion record tells of a certificate taken away by the admission keys an
admission names, its `certificate` and its `from`, only once the admissions are read, which
comes with the pull request that keeps the admissions in the store: until then such a deletion
record tells of nothing but what a record or an item of that kind names. The authorities
that stand under each key that stands are worked out for every state but no key and a store past
`StoreFormat.MaxKeys`, of which no key is judged, as a word is answered under them (§656). What
each kind means for access beyond that (steps 3 and 5) is built by the pull requests that bring
each kind's meaning, the authority's among them (§614).
The authority a database is served with is the single authority that stands in a store that is
whole, read from the version that passed, and every admission holds the target it is made on to
what the store then reads: the authority's entry, the version of it that passed, and that it
still stands alone, so a retirement written under a target already handed out refuses from that
target's next admission on. A database whose store holds no key is served nothing. *Held by*
`StoreReadingTests`, `StoreReadingStatesTests`, `StoreReadingKeysTests`,
`StoreReadingFormatTests`, `StoreReadingWholeTests`, `StoreInForceTests`, `StoreRoomTests`,
`StoreInRealDatabaseTests`, `StoreStatesInRealDatabaseTests`, `StoreAuthoritiesTests` and
`StoreAuthorityServedTests`, and in a real KeePass by `LiveStoreTests` and `LiveAuthorityTests`.

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
- **A merge does not bring the written version back.** On the store as decided, a merge with an
  unedited copy brought it back in no cell past the limit: KeePassLib trims the merged history to
  the limit, and KeePassXC 2.8.0 kept the newer entry whole.

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
2. **What takes access away is a union.** The revocations, the retirements and the removals in
   force are those present as records; those any summary entry present restates, whether or not
   that summary entry is in force; and those a deletion record tells of. A deletion record tells
   of a revocation, a retirement or a removal when its identifier begins with that kind's tag
   under the store key and carries the subject hash of a client key, an authority or a name the
   file knows (§609). So deleting a revocation for good, in any program that leaves a deletion
   record, changes nothing. A certificate taken away is united the same way: it is refused where
   a record, an item of any summary entry present or a deletion record states that it was taken
   away. A deletion record tells of a certificate taken away when its identifier begins with that
   kind's tag under the store key and carries the subject hash of `certificate:` and an admission
   key the file knows. The file knows an admission key from the `certificate` and the `from` of
   an admission, and from the `certificate`, the `from` and each key in `renewed` of a record or
   an item that took a certificate away. Such a deletion record whose identifier is that of a
   record present, or the `id` of an item, adds nothing to what that record or item states. Any
   other stands for a record whose content is nowhere, and is a statement of its own that names
   no renewal (§648). A keep record is united under the rules of §649.
3. **The versions of an object** are the records of kind `version` in force and the versions
   that the summary entries in force restate. Those that no version names stand, and the
   object's value is what all standing versions allow (§617). An object refuses as §617 states.
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
starts a store (§632). This rule was not run (§644).

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
deletion record tells of a version whose text is nowhere, a version that grants nothing and names
what was lost; then a write as §627 has it, which deletes every entry that is damage with a
deletion record and adds a summary entry over everything that passes and is in force.

**What its page has to state.** Whatever is not on the page when the operator confirms is not in
the new summary entry, so the press makes a loss permanent in that copy. From the deletion
records the page can show that a revocation, a removal or a version of a certain rule was
deleted, since an identifier holds its kind and a keyed hash of its subject, and for those the
press writes the refusal down. It cannot show a decision lost in both of its carriers with no
deletion record, as a script, a merge that keeps no record or a file written over leaves it. The
page states plainly that it cannot know what was deleted without a record and that confirming
makes the shown state the state, and the tab lists what is in force, so that it can be checked
first (§636).

**What was run.** The press left the store as decided wherever it was run after a summary entry
was deleted or deduplicated (`measured-store-as-a-whole` 3.4, 3.6). With a record for each
node and profile, the press wrote an empty rule for the row whose version was lost and for no
other row, and the switch refusing for a lost version of the switch
(`measured-rules-per-profile` 8). It was not run after a stop by an edited entry, and nothing
of it was run as a page (§644). Pressed after a loss that left no trace, it ends wider (§639).

**Where it does not apply.** Where the file holds no store key there is no stop to lift (§632).
It lifts neither several store keys (§649) nor a plugin that is held back (§652). Sealing is a
write, so a plugin that is held back, or that waits for the store to be raised, cannot seal.

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
question becomes which window puts back or takes away an entry of the plugin's group. That
question was not measured, and §644 names it as an obligation (reasoning, from §124).

**What a write of the plugin's own asks of KeePass's windows until that is measured** (this
document's own, read from `DESIGN.md` §124 and not run). A write is refused `503 busy` while
KeePass's progress window is open, and while the TAN wizard is open on the database where the
write ends in a save, as every write is today. It is refused as well while an entry dialog
holds an entry that the write would delete or set back, since that dialog puts its copy of the
entry's strings back at its OK. It is not refused on account of the store for Database
Maintenance or for a group dialog, since neither holds a copy of an entry. A write that still
adds to custom data, as a pairing's write of a profile name does until the names are records,
is refused under Database Maintenance for that part, as today. The pull request that builds
the first writes runs each window of `DESIGN.md` §124 against a write into the plugin's group,
and records what it found there and in that section.

### A write of the plugin's own §627

**When.** After an OK on the KPG settings tab; at a pairing or a renewal that the operator
approved; at an answer to a renewal that the operator gave to stand (S7); when a certificate is
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
   where §650 has the write carry it (proposed).
3. **What a record present replaces is deleted, with a deletion record**: every version that a
   present version names; every profile addition that does not name every known removal of its
   name; every admission whose client key is revoked or whose issuer is retired; every authority
   that is retired; and every summary entry present, with every summary entry those name. A
   deletion record is written as well for a replaced record that this copy does not hold, so
   that the record stays out of force when a merge brings it. An admission whose certificate was
   taken away goes the same way (proposed, §648). **What this copy holds is every entry of the
   tree, by its identifier, a record or not** (this document's own): an entry with every string of
   the store gone stays where it is, with no deletion record, which a merge would take as the
   entry deleted, and no record is added under the identifier of an entry the tree holds. The
   plugin deletes no entry that it cannot read as a record of its store. *Held by*
   `StoreWriteTests` and `StoreWritesInRealDatabaseTests`.
4. **A summary entry is added** (§611) over what is then in force.
5. **Every record that counts only by its history is set back** (§628).
6. **The database is saved, or marked as changed**, as today for the same act (below). A write
   that is saved is believed as today.

**A deletion by the plugin never takes anything away.** What step 3 deletes is already replaced,
refused or restated, and the plugin never deletes a record in order to revoke, retire, remove or
narrow.

**Which writes save** (this document's own, from what each act does today). A pairing, a
renewal and a certificate that its client gives up answer a client, so each is saved through
`MainForm.SaveDatabase`, read back and verified before it is answered, `DESIGN.md` §119. The
settings dialog's OK and the answer to which authority to keep are the operator's, at
KeePass's own window: their records are added in memory, the database is marked as changed,
and the operator's save writes them, as the tab's changes and a kept authority are written
today, `CA.md` §204. They are in force in the gate's copy from that moment, and are lost with
every other unsaved edit where the database is closed without saving.

**Every write goes through the writer's guard**, the settings dialog's OK among them, which
today is applied with no question asked. A write is made on KeePass's thread, after the
questions every write asks there, `DESIGN.md` §119 and §124, and after the store was read in
that same turn (step 1). A write of the operator's that is refused there adds nothing, and the
log names what was not written and why. The settings dialog's OK can meet a save of the plugin's
own, pumping under the dialog while a pairing lands, and is refused there like any other write
(§644 item 30). Where the dialogs that would take a write back are not watched, which the
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
carries more than fits, and the log names how much fits. From half of either ceiling the
reading states how far the store has grown, for the log where a reading first finds a store it
serves there and at every write, and for the tab (§643). *Held by* `StoreRoomTests`: a store at
each ceiling has no room, the same store a summary entry or an item short of it has room, and
two stores that each have room, synchronized, are judged by what the next write would name.

**A write that does not land is taken back out**, as today: the entries it added are removed
with no deletion record (the design paper 10), and the entries it deleted are put back with
their deletion records removed (reasoning). A record that it set back is put back as it was
edited, with the history it had; a group that it made is removed; and a deletion record that
it added for a record this copy does not hold is removed. *Held by*
`StoreWritesInRealDatabaseTests`: after a save that fails, the database holds the entries, the
histories, the group and the deletion records that it held before the write, and reads as it
read before.

**The settings tab still buffers.** `PendingDatabase` holds records to add where it holds keys
today, OK applies them in a single turn, and Cancel drops them. The merges the tab registers
today become properties of the records (the design paper 10, not run): a removal of a name
defeats an addition that a pairing wrote underneath the dialog, since that addition did not see
it; **Allow renewal** is an answer whose `after` is what the page showed when it was pressed, so
an answer the renewal dialog wrote underneath is made apart and the refusal stands; and a
revocation is an addition that cannot fail in the buffer.

**What the buffer holds, and what OK does with it** (this document's own). Each press on the
tab composes the records of its act at the press, from the store as the page showed it, and
puts them into the buffer: a revocation, a certificate taken away with the renewals the page
showed, a retirement, an authority with its certificates and its keys. The pages read the
store with those records beside it, so a page shows what OK would leave. An authority made on
the dialog and retired on the same dialog leaves the buffer with its retirement and reaches no
database, as today, and where that retirement held the store key the retirements left in the
write hold it, so a Recreate and then a Delete on one dialog write what the Delete alone writes
(this document's own). At OK the acts are held, in the order they were pressed, to the store as
it stands then. An act whose ground went meanwhile, as a Recreate of an authority that no
longer stands, is left out, and the log names it, and so is an act composed against a store key
other than the key that stands then, or composed where no key stood while a key stands then: a
Create composed under a key that went since never starts the store again under it (§632). A
press is planned with every act held, against the store as it stands, as OK plans them, and is
refused with nothing held where OK would refuse them all or leave it out. Every other act's
records are written in a single write, with the summary entry over what is then in force. A
revocation and a certificate taken away have no such ground, and are always written. Cancel
drops the buffer, and a store key that was made on the dialog goes with it. **What an act takes
out of the custom data waits with the act** (this document's own): the admissions a Recreate or
a Delete takes away read as gone on the page while it waits, and leave the custom data at OK
only where the write took the act (§614). *Held by* `PendingRecordsTests`: a revocation pressed
beside a Recreate whose authority was retired underneath the dialog is written, and the
Recreate is not; a Recreate refused or left out at OK takes no revocation away; a Recreate and
then a Delete write what the Delete alone writes; a press that OK would refuse with the acts
held is refused with nothing held; a Create and a Delete on the same dialog write nothing and
change nothing; and a page reads a revoked client as gone before OK. And by `StoreWriteTests`:
an act composed against another key, or where none stood, is left out; a retirement dropped
with the authority it retires leaves its hold on the key to the retirements left; and an entry
the tree holds gets no deletion record and no second entry.

**What is built.** The writes of the authority's commands (§614): Create, Recreate and Delete
at the settings dialog's OK, buffered by `PendingRecords` beside the keys the tab buffers in
custom data and written before them, and keeping an authority. Each is planned by `StoreWrite`
against the store read in the writer's turn, steps 1 to 5 with the room held at the plan as at
the reading, so that a write the room allows is never refused for a ceiling, and applied to the
database's entries by `StoreEntries` on KeePass's thread through the writer's guard, which marks
the database as changed and saves nothing. A write the application cannot finish is taken back
out at once; taking back a write that was saved and did not land comes with the writes that
save, a pairing's and a renewal's. A revocation is not an act yet, so the buffer holds the
authority's acts alone. *Held by* `StoreRecordTests`, against the records of the measured code
(`tests/Shared/StoreRecordVectors.json`); `StoreWriteShapeTests`, against its writes
(`StoreWriteVectors.json`): both sequences played, and each step's deletions, deletion records,
records present, summary entries, store keys and answer compared by kind and by count;
`StoreWriteTests`, each step of the plan alone, the ceiling at its boundary and a record written
once among them; `PendingRecordsTests`; and `StoreWritesInRealDatabaseTests`, the group a write
goes into and a write applied to a database read as the same write applied in memory among
them.

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
(`measured-rules-per-profile` 1). What else goes with it, and how it meets the writer's
refusals, is S15.

**What it costs.** A rule is lost if KeePass ends, or the database is closed, before a save, as
any unsaved edit is (§642).

**Considered and not taken: holding the rule in the plugin's memory and adding it at the event
that precedes a save.** A plugin's addition at that event reached the file in every run that
tried it. The event is also raised inside the save that ends a synchronize, with the merged
database in memory (`measured-desktop-windows` 6.2), so a version made there is made after a
merge, beside versions of another gate that the operator never saw.

**What was measured.** A rule waiting in memory was run in the model only
(`measured-store-as-a-whole` 1). On files and in KeePass's window it was not run (§644).

**The editor no longer writes into the dialog's copy of custom data.** The grant editor keeps its
change in a buffer of its own and applies it when the node's dialog closes with OK (the design
paper 10). What the editor shows for a rule that stands in several versions is §657.

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

**What is built.** `split`, `outdated` and `stopped`, the last also for a store that has no room
for its next write (§643), answered before a route is chosen by the target of a database the
gate serves, and, ahead of `undecided`, by each target of a database that holds several
authorities, under the host certificate of the authority that stands or of each of those
authorities, worked out under each key that stands (§614). `lost`, and the memory of what a gate
last served with: the host certificate and the gate key of the authority served, copied as the
target is made, carried by every entry made for the database in its place, and zeroed at the
lock, at the close, when KeePass ends, and where a store that holds nothing back gives no
authority to answer with, as where the authority served was deleted on the tab. Where the
reading gives no authority to answer with and the store holds the gate back, that memory
answers the store's word, `lost` for a file with no key, under its own gate key, and judges no
certificate and reaches nothing; a lock lets it go with no responder. A database opened or
unlocked with no store key, and a database whose store holds no authority that stands, resolve
no name. *Held by* `HeldBackGateTests`, `StoreHeldBackGateTests`, `StoreWordsTests` and
`LockInRealDatabaseTests`: a served database whose key carrier is deleted answers `lost` with
the host certificate and gate key it served with, and nothing after a lock, and a database whose
authority was deleted answers nothing. `frozen` comes with the release that first moves the
format number (§652), and the routing document declares it and the reference client knows it
already. *Held by* `StoreWordsTests`, `StoreHeldBackGateTests`, `HeldBackGateTests`,
`UnavailableWordsTests` and `StoreInForceTests`, the order of the words by
`StoreReadingStatesTests` and `StoreRoomTests`, and in a real KeePass by `LiveStoreTests`.

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
- **Its clients page takes a client away, or a single certificate** (§615, §648).
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
  why, as under a word. Built, and held by `AuthorityPanelTests` and `OpenDatabaseTests`.

Nothing of the tab was run.

### The grant editor §657

**A rule that stands in several versions shows as that many rows, bound together** (a later
decision, taken from pictures of a prototype in KeePass's own group and entry dialogs). What
the operator sees is decided. How it is drawn is proposed, and belongs to the pull request that
builds it.

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
dialog's event that precedes a save is not used for this.

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

**A row removed under a switch that refuses stays in the table, as an empty rule** (a later
decision, §617). Where the node's switch refuses any profile not named, the X on a profile's
row writes a version that grants nothing, so the row stays, shows the mask with no letter, and
is there until the operator removes it again. Where the switch lets the walk go on, the X takes
the rule off and the row goes. The X on a row that is an empty rule already writes nothing
while the switch still refuses, as run (§617), so that row goes by a removal made once the
switch lets the walk go on. How the table shows the operator that such a row cannot be taken
off yet is not fixed, and is part of S9.

Nothing of the editor was run on the store.

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
(`measured-store-as-a-whole` 2.1, 2.4).

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
`measured-decided-store` 5).

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
2.4).

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
took and renewed, by taking that certificate away as well or by revoking the client (§648, not
run). *What covers it:* a Synchronize at every gate. *Measured:* in the model, and on
real files for the renewal (`measured-store-as-a-whole` 2.4, 3.1). Two KeePass instances with the
plugin were not run.

**A node with no rule of its own comes to sit, or stays, under a grant** (row 9). *Who:* nobody
has to make a mistake. KeePass's Synchronize takes the later of two moves, and a KeePassXC merge
of an older copy brings a deleted node back. *What happens:* the profile reads the node. *What
covers it:* a rule on the node itself, which held in every cell of 90
(`measured-store-as-a-whole` 3.2). Access may rest on a node's place (decision 9). *Measured:* on
other options' records and on generic carriers; not replayed on the store.

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
(`measured-store-as-a-whole` 3.4).

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
database for the next save it was not measured (§644).

**Two files that hold the same entries of the store answer differently** (row 16). *Who:* every
fault but an overwrite, in a second model of the design. *What happens:* 3 pairs in 1,500
histories; the pair printed admits a client in a file and refuses it in the other. *What covers
it:* nothing found. *Measured:* in that model, before the decisions of 2026-11-16, and not
replayed on the store as decided.

**Rules changed apart at two nodes add up to more than either copy's table gave.** *Who:*
nobody has to make a mistake. A copy takes a narrow rule off a node, or turns the node's
switch off, so that the profile gets there what arrives from above. Another copy, apart,
widens the rule above that node, at a time when the narrow rule, or the switch, still stood
there. Each table kept the node narrow for the profile. *What happens:* once the copies meet,
the narrow rule is taken off and the rule above is the wider rule, since each is an object of
its own with a single standing version. The profile gets at the node what the wider rule
gives, which neither table gave. The same holds in the whole-rule form, since the rules of two
nodes met in neither form. *What covers it:* nothing in the store, which reads each node's
rule by itself (§617). The decision on a removed row does not touch it, since nothing is
removed there under a switch that refuses. It is the cost of rows and of nodes that do not
meet, and what is left of S34: the pull request that builds the rules puts it before the
project, and nothing here decides it. *Measured* (`measured-rules-per-profile` 7): on real
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
no rule.

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

**Two gates change the same profile's rule at a node apart** (row 22). *What happens:* after any
merge the profile gets what all versions allow, until the operator has removed versions in the
grant editor (§657). Narrower. Two gates that each remove a different version apart write
again what the other removed, and the operator removes versions again (§617). Narrower as
well. Two gates that change different profiles at a node end as decided, with nothing to
settle (§617). *Measured:* on real files, in the whole-rule form, where any two changes to a
node's rule met (`measured-store-as-a-whole` 3.1), and with a record for each node and
profile, for the same profile and for different profiles changed apart
(`measured-rules-per-profile` 4, 5).

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
decision): its admission, and the record that took it away.

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
   declared; §633 states what it does not show. Owed again by every pull request that adds a route
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
   (`StoreStatesInRealDatabaseTests`); the rest of this item stays owed. *Could overturn*: the
   history rule as worded, and what §632 states of a store started again.
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
   before a save. Owed by the pull request that builds §629. *Could overturn*: decision 11 as
   worded.
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
    entry of a file, by the pull request that builds the reading,
    through the real router and vault, and recorded in §620, in `DESIGN.md` §119 and in
    `FINDINGS.md` section 22: it overturns nothing. What stays owed is the same run on a
    machine that is not a developer's, and a database of more entries than 10,000.
13. **The page that seals the store again**: what it shows after each kind of loss. Nothing of it
    was run as a page.
14. **Which of KeePass's windows takes back or takes away an entry of the plugin's group** while
    the plugin writes (§626). This item is this document's own, read from `DESIGN.md` §124 and
    from no report. Owed by the pull request that moves the first write onto the store, and
    §626 states what such a write asks until then.
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
16. **A single certificate taken away** (§648), of which nothing was run: a renewal and the old
    certificate given up at a gate while another gate holds a copy from before; a renewal made
    from that certificate at the other gate in the meantime, and a renewal onward from that,
    also once the certificate between was given up at that gate; the same certificate taken
    away at two gates that had read other renewals; the operator's single removal; the record
    deleted and edited by hand; an admission whose parent admission is
    lost; a revocation of the same client beside it; and each merge. Owed by the pull request
    that builds §648. *Could overturn*: the rule about renewals (S29).
17. **Create refused on a stopped store** (§624), which the measured code does not do. Owed by
    the pull request that builds the authority's commands on the store.
18. **A version that has to read a single way** (§607, §608): a string beginning `KPG.S.` that is
    not four digits, a line feed in a part, a member that appears twice. Each fails in
    `StoreReadingTests`, over composed versions; none was run through another program's save.
19. **The format number, the version record and a kind the plugin does not know** (§647, §621),
    of which nothing was run: the highest of each after each program's merge; an older plugin
    on a store that was raised; a newer plugin on an older store, before and after the operator
    raises it; a write refused in between; a deletion record that tells of a version record; a
    record and an item of a kind the plugin does not know; a gate on a stale copy; and a store
    started again from a store that holds the plugin back, with the earlier store coming back
    by a merge. The pull request that builds the store's core runs, over real databases, a
    version record of a later format and its deletion record arriving by Synchronize, and a
    record of a kind the plugin does not know with no version record present; and over
    composed stores, the item, the deletion record that tells a number and one that does not.
    The rest is owed by the pull request that builds the writing of §647.
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
    `LockInRealDatabaseTests`); `lost` in KeePass's window (`LiveLostTests`) and `frozen` stay
    owed, `frozen` by the pull request that makes the gate answer it.
23. **The grant editor's rows for a rule in several versions** (§657). The pictures the decision
    was taken from are of a prototype with rows that were made up, and not of a store: two and
    three versions, scrolled, under a high contrast setting, and with what removing a row and
    the dialog's OK write. Owed by the pull request that builds the editor on the store.
24. **Rules as a record for each node and profile** (§617). This item held the run owed before
    the rules are built: the run of the store as a whole, made again with a rule record for
    each node and profile and the switch as a record of its own. It was made
    (`measured-rules-per-profile`; the numbers in parentheses below are its sections), on
    files made and changed by KeePassLib 2.62 and merged by KeePassLib and `keepassxc-cli`,
    every part twice with the same output: the merge cells of the store as decided, over
    KeePassLib's five methods and four KeePassXC releases, each also in the whole-rule form;
    the same deletions and edits of an entry of the store, with the switch as a further kind;
    the same random histories, and random histories of acts on a single row and on the
    switch; and the model's searches with that form. **What it showed**, by what the item
    asked for. No cell is newly wider: of 396 merge cells 16 ended wider, the same cells in
    both forms, each an import or a stale copy, and of the 724 files of the random histories
    no file ended wider with a record for each profile that did not in the whole-rule form;
    the class of a single merge cell and of six of those files differs between the forms,
    and none of them grants more (3). Two copies that change different profiles at a node
    ended as decided with no row to settle, in 112 of 112 cells that are not imports, where
    the whole-rule form ended narrower in 80 of 96 (4). The same profile changed apart gave
    what all versions allow, for two versions and for three (5). An empty rule and a rule
    taken off stayed apart through every merge and every edit, and not through every loss:
    after the loss of a version that took a rule off, sealing the store again writes an
    empty rule, which is narrower (6). The switch in versions made apart refused, in the
    single sequence run (7). A deletion record still told which row's version was deleted
    (8). Rows changed apart added up to more than either copy gave in 67 of 200,000
    histories of the model, against 298 in the whole-rule form, and a row removed under a
    switch that refuses, written as an empty rule, did not (7; §639). The model's searches
    failed in the same kinds as for the store as decided; their counts are the earlier
    record's in 20 configurations of 24 and differ by a single failure in each of the other
    four, in histories that were not traced; and with acts on a row and on the switch, and
    no loss of the authority's entry, no shortest failing history holds a row or a switch
    (3). **What stays owed**, by the pull request that builds the rules: more than two
    profiles and two letters in a file, and the switches of a rule; a rule on a group
    between the root and the node; a row of a name off the list whose records and items are
    all gone, which the reading that was run cannot tell from a deletion record; two
    profiles whose tags meet, in the form proposed for S8; the read in the plugin's own
    code; how the `after` lists grow under repeated edits of a row; the histories in which
    the model's counts differ; the parts of the earlier run that were cut from this run,
    which section 12 of the report lists, with KeePassXC releases other than 2.8.0 for the
    edits, the deletions and the random histories; and the grant editor, a second KeePass
    with the plugin, and a device (items 23, 7 and 5). *Could overturn*: the form of S8, and
    whether what is left of S34 can stay accepted.
25. **What the Profiles page counts** (§636). Its count of the groups and entries holding a rule
    for a profile, `DatabaseNodes`, leaves the recycle bin out and not the plugin's store, which
    the API leaves out beside it (§633). Owed by the pull request that brings the tab's listing
    of the store.
26. **The store asked again where a client writes into it.** A pairing, a renewal and a client's
    own revocation are judged against the store where the connection arrives, and not again
    where their write is made, since a rule governs none of them and the router asks the store
    again only where a rule is asked; one that waits for its dialog meets a store that came to
    hold the gate back meanwhile only at its next request. Owed by the pull request that moves the
    admissions into the store: each of the three asks the store again at its write and answers
    the store's word there.
27. **What the tab shows past the ceiling on store keys** (§649). A store past it is split with
    nothing judged, and the dialog that keeps a store has nothing to offer, since no keep record
    is read: the tab states the number of keys found and the number of entries hidden, and that
    the entries that brought the keys are to be deleted first. Owed by the pull request that
    builds the tab's listing of the store.
28. **Copies that keep different authorities** (§614), of which nothing was run with the store
    key in each retirement: each copy keeping another authority, then brought together by
    Synchronize, by each import method that keeps identifiers and by KeePassXC, read before and
    after a Create. Owed by the pull request that keeps the authority in the store. *Could
    overturn*: what keeping an authority writes (S11).
29. **A string's protection taken off and set back** (§628), of which nothing was run: the
    protection of an authority's content taken off in KeePass's entry dialog, the plugin's
    next write, and each merge with a copy that still holds the unprotected version. Owed by
    the pull request that builds the first writes. *Could overturn*: the answer to S18.
30. **The operator's write behind the plugin's own save.** The settings dialog's OK pressed
    while a save of the plugin's own pumps under the dialog, as a pairing's does, is refused by
    the writer's guard, and nothing the tab held for the store is written (§627): the log names
    it, while the dialog closes as accepted. The write should wait for that save to end and then
    be made, held to its ground as it stands then. This item is this document's own, read from
    the code and not run. Owed by the pull request that keeps the admissions in the store,
    which builds the writes that save.
31. **The longest item a summary entry carries, against the room.** The room holds the content of
    the summary entry a write would write to the ceiling for a string, and not the items that
    entry carries, each a string of its own, and an item is longer than the record it restates:
    a version's item is 14 characters longer than the version record's content, and a keep's
    item 11 (this document's own, computed from the members each carries and not run). A version
    record whose content is 262,131 to 262,144 characters is written, since it fits, and from then
    on every write is refused for the ceiling while the store is served, against §627. Nothing
    writes a version record yet. Owed by the pull request that builds the rules: the room holds
    the longest item it would carry to the ceiling, or a version is composed only where its item
    fits.

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
every part of the store is built. Until then a build still reads the profile names and the
rules where they were kept before, so the steps would not hold of that build, and the path of
the last step can be verified only against a plugin that reads nothing of what is cleared
(§644).

**No client program has to change for 0.3.0 but a program that named fields of its own with the
prefix `KPG.`**, which is why it is a minor release. Such a field is out of every route from
0.3.0 on (§633), so such a program renames those fields before the upgrade, and the release
notes mark that as breaking, as `API.md` §411 records. `API.md` §411 has a change break a client
when a program that worked before has to change. Pairing again is
the operator's act and the client's ordinary command. A word that §656 adds is answered under
`503`, and a 0.2 client takes a word it does not know for the gate being unavailable (read from
the client). So a 0.2 client works with a 0.3 gate, as §411 has it. §411 also counts what a
certificate or an admission record carries: the admission changes where it is stored and what
it names (S4, S29), and that holds only while nothing of it changes what a client is sent. The
pull request that builds the admission checks it against §411.

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
and the admissions in the store, and is fixed in the same manner.

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
  today's record, `issuer` held to the anchor pin, and a record that does not read admitting
  nobody, §615.
- **S5. The revocation's content.** Fixed for the first writes: a single revocation, holding
  the client key alone, §615.
- **S6. A certificate given up by its own client.** A record of a kind of its own, §648.
- **S9, in part. How the grant editor shows a rule in several versions, and what removing a
  version writes.** As rows bound together, with no message box and no dialog held at OK,
  §657. Removing a version writes again each version that is left, fixed as measured, §617.
  What is left is below.
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
- **S15, in part. Where a rule waits between the dialog's OK and the save.** In the database, in
  memory, added at the OK, §629. What is left is below.
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
- **S22. What the operator's documentation advises for what 0.2 left in the file.** The steps
  of §645, in the release notes and in `docs/`.
- **S23, in part. The form of the kinds that were in no run.** Fixed for the core: the kinds
  `withdrawn`, `kept` and `format`, with what the core reads of every kind, §658; the keep
  record, §649; the version record, for good, and the first format number, §650; what a
  deletion record tells of each, §623. A rule and a switch are records of kind `version`, as
  measured, with an object of a type the core knows, §658. What is left is S8, S7 and S29,
  below.
- **S24. Two admissions that name the same certificate.** Fixed for the first writes: a
  certificate that several admissions in force name is admitted by none of them, §615.
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
  whose certificate of origin is neither admitted nor taken away not counting, §648.
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
- **S34, in part. Rows changed apart at a single node, under a switch that refuses.** Removing
  a row there writes an empty rule, and the row shows as an empty rule until the operator
  removes it again, §617 and §657; the sequence it closes is in §639, with the run that
  confirms it. As run, removing that empty rule again while the switch still refuses writes
  nothing, §617. What is left is below.
- **S35. A store that has no room for its next write.** The gate serves nothing for that
  database, answered `stopped`, until the store is started again, and the answer is looked at
  again once a compaction exists, §643.

**What the first pull request that builds the store settles.** Every item of this group is
fixed, and the list above names where: S1, S12, S13, S16, S20, S23, S26, S31 and S33.

**What a later pull request settles.**

- **S7. The answer to a renewal.** Measured: an object `renew:` and the client key, with stand-in
  texts for forever and for allowed. Today the answer is about a certificate and can hold a date,
  `PAIRING.md` §312, and the design paper names the certificate and the three answers forever,
  until a moment, and allowed, of which the strictest standing answer wins. Not fixed: the
  object, the text of each answer, and how until a date is written and compared. Not fixed
  either: an answer that could not be written when it was given is written today by the next
  save of the database, whoever starts it, §312, and evaluation 4.6 does not list that write.
  Has to satisfy: two answers made apart give the stricter; an answer for this session has no
  record; no merge decides by a time.
- **S8. The form of a rule's version and of the switch's** (§617). Decided: a record for each
  node and profile, and the switch as a record of its own. The measured code wrote a version of
  a node's whole rule, with stand-ins for its document. Not fixed: how the object names the
  node and the profile, and the node alone for the switch; how the letters are written, as the
  mask the editor shows or as the words today's document holds; the text of a version that
  takes a rule off; and the text of the switch's two values. Has to satisfy: an empty rule and
  a rule taken off are told apart in the text and through every merge; a profile's name in an
  object is read as the list of profile names reads it (§616); a deletion record still tells
  which node's and which profile's version it names, without the reading having to try every
  node with every name at every request (S20); and a rule's form is fixed with the format
  number it is first written under (§650). **Measured, and proposed from it**
  (`measured-rules-per-profile` 1, 8). The run wrote a row's object as `rule:`, the node and
  the profile, with the subject hash over the whole of it, and the switch's as `switch:` and
  the node; its texts held two letters, read and write, and fix nothing of the product's
  text. In that form a deletion record tells the row, and only by what the condition on a
  deletion record rules out: the reading tries each node of the tree with each profile name
  the file knows, which is 12,108 keyed hashes for a tree of 2,000 nodes and four names, and
  grows with every node, ruled or not. The form
  proposed takes the subject hash over `rule:` and the node alone, and puts a keyed tag of
  the profile's name into bytes 9 and 10 of the identifier, which are 0 for every version so
  far (§609). It gave the same lines as the form run in 4 of 4 sequences, 24 of 24 lines, for
  a hash for each node and a tag for each name: 4,048 keyed hashes for the same tree, and
  about half the time of the read in the probe. The switch would sit under the same subject
  as the node's rows, with a fixed value of its own in those two bytes (reasoning, not run).
  A tag of two bytes meets the tag of another name by chance about once in 65,536 for each
  pair of names, and such a deletion record would refuse both rows, which is narrower
  (reasoning). It is a proposal: the pull request that builds the rules fixes the form, and
  changes the row of `version` in §658 with it.
- **S9. What the resolver and the editor need for versions made apart** (§617, §657). Decided:
  what all versions allow, and what the operator sees. By reasoning: versions that grant give
  the letters all of them hold; the switch in versions made apart refuses. Fixed as measured:
  removing a row of a group writes again each version that is left (§617). Not fixed: the
  resolver's change, which has to carry a limit up its walk where a version that grants stands
  beside a version that takes the rule off; how the table shows a row that is an empty rule
  under a switch that refuses, which a further removal does not take off (§657); and the
  drawing where the prototype left it weak: a bold dot that is faint, bold letters with less
  contrast on the red, a table of four rows, and a high contrast setting. Has to satisfy: the
  result is never wider than any of the versions, for every letter; a switch of a rule without
  a verb still grants nothing; what tells the rows apart does not rest on color alone; and a
  removed row stops standing in every copy the write reaches. The form fixed meets the last
  condition except where two copies each remove a different version apart: those versions
  then stand again, which is narrower, and no form that was run meets the condition there
  without ending wider (§617).
- **S15. What is added at a node dialog's OK, beside the versions of the rows changed** (§629).
  In the model a summary entry is added with it. Not fixed: whether the addition sets edited
  records back, as a write of the plugin's own does (§628); and how it meets the writer's
  refusals, since it is made while KeePass closes the node's own dialog. Has to satisfy: in
  force from the OK; no save started by the plugin and nothing hooked into saving; Cancel
  discards; a row that was not changed adds nothing; and a rule narrowed while a save runs does
  not leave the file with the older rule (row 15 of §639, whose cover names the plugin's group
  written last in the file).
- **S21. A ceiling on profile names, and on the profiles ruled at a node.** Today the list holds
  at most 32 names, which is `JsonBody`'s ceiling for an array, and a rule document holds at
  most 31 profiles, `DESIGN.md` §136. With an entry for each addition and a record for each node
  and profile, the format has neither ceiling. Not fixed: whether a ceiling is kept for either.
  Has to satisfy: what a ceiling refuses denies and never cuts short; and the grant editor's
  table and the summary entry's item names (S13) hold what the ceiling allows.
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
names where. What stands here is what those answers left open.

- **S34. Is it accepted that rules changed apart at two nodes can add up to more than either
  copy's table gave** (§617, §639)? The rules of two nodes are objects of their own, so a copy
  that takes a narrow rule off a node, and a copy that widens the rule above it while the
  narrow rule stood, end with the profile getting at that node what the wider rule gives. The
  answer given to the same question at a single node does not reach it, since nothing is
  removed there under a switch that refuses. Measured, in both forms (§639): on files it ended
  with more than either copy gave in 16 of 18 cells of the sequence run, with a record for
  each node and profile and in the whole-rule form alike, and in the model in 67 of 200,000
  random histories against 298 in the whole-rule form. The pull request that builds the rules
  puts it before the project, and nothing here decides it. The answer named so far is that it
  is accepted, as the cost of rows and of nodes that do not meet. Has to satisfy: two copies
  that change different profiles at a node still do not meet; and an empty rule and a rule
  taken off stay apart for the operator.
