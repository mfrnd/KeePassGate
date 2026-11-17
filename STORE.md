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

> **Decided, and not built.** This document is the contract for the store of release 0.3.0. Release
> 0.2.0 ships the store `CA.md` §204 describes: the database's own custom data, and a `KPG` key in
> the custom data of groups and entries. Nothing below describes what the plugin does today. Each
> pull request that builds a part of this store brings `CA.md`, `DESIGN.md`, `PAIRING.md`, `API.md`
> and `docs/` into line with the code it adds. Until then those documents hold for 0.2.0, and this
> document holds for what replaces its store.

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
  left (§643), and the recycle bin (§619).
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
| A rule lives in the custom data of its group or entry, and the grant editor writes into the dialog's own copy | a rule is a record in the store that names its node, and nothing is on the node | KeePassXC never merges a group's custom data, and two programs delete it at a save, §603 |
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
member `v` and its value (S1); the nonces and the store keys, derived from fixed texts (S2); the
authority's content, which held no certificate (S3); the admission's members (S4); a revocation
written for each certificate (S5); the answer to a renewal, in its object and its texts (S7); the
rule documents (S8); a title made of the kind and a piece of the identifier (S14); and the
reading of several store keys, which holds every key found and counts under each, where §649
departs from it. The kinds of record that this document adds were in no run at all, and their
formats are to settle: a certificate taken away (§648), a keep record (§649) and the version
record (§650).

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
guard covers (§635). Which group is written into when a tree holds several groups with the prefix
is S11.

### A record §607

A record is an ordinary entry, written once.

| Field | As measured |
| --- | --- |
| Title | `KPG`, the kind, and the first eight hexadecimal characters of the identifier, separated by spaces. Nothing reads it (S14) |
| User name, URL | present and empty |
| Password | present, empty, protected |
| Notes | `KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.` |
| Auto-type | off for the entry |
| `KPG.Kind` | the kind of record: `authority`, `retired`, `client`, `revoked`, `profile`, `removed`, `version` or `head`. The kinds of §647, §648 and §649 were not measured and have no name yet (S23) |
| `KPG.Data` | the content: a single flat JSON object whose values are all strings |
| `KPG.Mac` | the check, §608 |
| `KPG.S.0001` and on | on a summary entry only: an item for each decision it restates, §611 |

**Custom strings are the carrier** because every program that was run keeps them, with their
values and their protection (the design paper 2, T13). `KPG.Data` is protected in an authority's
entry and in a retirement that holds the store key, and in no other record.

**`KPG.Data` is read strictly**: an object, every value a string, and nothing else. The measured
code wrote it with its members sorted by name and no white space. Nothing reads that order: the
check is over the text as it is stored. **A member that appears twice makes the version fail**
(this document's own): the probe's reader kept the last of two, which lets two readers take two
contents from a single text that passes, and `JsonBody` refuses a repeated name today. An item of
a summary entry is read the same way. Every record holds two members beside its own: `v`, which
the measured code wrote as `2` and never read (S1), and `nonce`, 16 random bytes as 32 hexadecimal
characters. The nonce is what gives every act of writing an identity of its own: a rule set, taken
away and set again to the same text is a new record, so no record can arrive already replaced
under an earlier identifier (the design paper 3.3). The probe derived its nonces from a fixed
text, to make its runs repeatable; the product draws them at random (S2).

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
passes (S18).

### The identifier §609

A record's identifier is made of its content, which is how a copy is told from the record.

```text
bytes  1 to 3    the first 3 bytes of HMAC-SHA256(store key, "tag" LF kind)
bytes  4 to 8    the first 5 bytes of HMAC-SHA256(store key, "subject" LF subject)
bytes  9 to 10   the height, big-endian: 0 for every kind but a summary entry
bytes 11 to 16   the first 6 bytes of the check
```

| Kind | Subject |
| --- | --- |
| `authority` | `authority:` and the authority's `id` |
| `retired` | `authority:` and the retired authority's `id` |
| `client`, `revoked` | `client:` and the client key |
| `profile`, `removed` | `profile:` and the name |
| `version` | the object it is a version of: `rule:` and the node's identifier as 32 hexadecimal characters, or `renew:` and the client key |
| `head` | `store` |

The subjects of the kinds that were not measured are S23.

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

**The height has two bytes**, which the measured runs never came near (S12).

**The identifier holds six bytes of the check**, as measured, and those bytes are all that binds
an entry's content to its identifier. That is enough against a copy, and against anybody who
does not hold the store key, who cannot compute a check at all. It does not bind against
somebody who holds the key: that person can search, away from the database, for another content
whose check begins with the same six bytes, which is about 2^48 tries for a chosen entry, and
put it into the entry in place of what the plugin wrote (reasoning). With the file in hand that
person writes any record anyway (§639). The search matters for somebody who holds the key and
can do no more than change fields, as a client of a 0.2 gate can (§654). The length is S31.

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
| `KPG.S.nnnn` | an item for each decision restated, a flat JSON object of strings. The items are sorted by their text and numbered from `KPG.S.0001` with four digits (S13) |
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
| `{"kind":"version",...}` | `id`, `object`, `after`, `doc` | a standing version of a rule or of an answer to a renewal, whole, with its text |

It restates every revocation, every retirement, every removal and every standing version. It
holds no authority, no admission and no profile addition, because losing any of those admits
nobody. Each alone, the records or the newest summary entry, is enough to compute what takes
access away, and the reading unites them (§623).

**What the kinds that were not measured add to it** (proposed; the items' forms are S23). Each of
them takes something away or holds a plugin back, so each is restated: a certificate taken
away, by an item that names the certificate and the renewals its record names (§648); a keep
record, by an item that names the keys not kept (§649); and the highest format number and the
highest plugin version the store states (§650).

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

### Authority and retirement §614

**An authority** is a record of kind `authority`, protected, holding today's authority document
(`CA.md` §222: the authority, the host certificate and the gate key) and the store key. As
measured its members are `id`, the authority's name, and `storeKey`, the key as 64 hexadecimal
characters. The probe's authority held no certificate. How today's document and `id` are written
into the record is S3.

**A retirement** is a record of kind `retired` with the member `authority`, the `id` of the
authority it retires. It is final: nothing takes a retirement back. A retirement written by
Delete, with no authority after it, also holds `storeKey`, and is then protected (§632).

**What each command writes.**

- **Create**, where no authority stands: an authority under the store key that stands, or under
  a new key where no key stands. The first Create also makes the group. Create is refused while
  the store is stopped (§624), while several store keys stand (§649), and while the plugin is
  held back or the store waits to be raised (§652).
- **Recreate**: a retirement of the authority served, and a new authority that holds the same
  store key.
- **Delete**: a retirement of the authority served that holds the store key.

The entry of a retired authority is deleted with a deletion record in the same write (§627),
which takes its private key out of the file (the design paper 9).

**Which authority is served** is decided by the retirements, as today, among the records under
the single store key that stands (§649). The authorities that stand are the `authority` records
that are not beside a deletion record naming them and that no retirement names, where a
retirement counts as an entry, as an item of any summary entry present, or as a deletion record
that tells of it (§623). A single standing authority, in a store that is whole, is served. No
standing authority serves nothing. Several standing authorities serve nothing, every connection
is answered `503 undecided`, and KeePass asks the operator which authority to keep, as today,
`CA.md` §204 (evaluation 6, row 20). What keeping an authority writes is S11.

**A retirement under another store key retires nothing.** An authority and a retirement that a
file from outside brings under a key of its own are under a second key, and a second key stops
the gate until the operator keeps a store (§649). The measured code counted a retirement under
any key it held, which let a file from outside retire the database's authority by naming its
`id`, and that `id` is public where it is the digest of the authority's certificate (read from
the measured code, not run).

### Admission and revocation §615

**An admission** is a record of kind `client`, a record for each certificate admitted, holding
today's record text (`CA.md` §223). As measured its members are `certificate`, `client`, the
client key, `issuer`, the `id` of the authority that issued it, and `profiles`. The client key is
today's: the admission key of the certificate the client paired with, copied into every record a
renewal writes, `CA.md` §217. How today's record text is written into the record, and what
`issuer` is held to, is S4.

**A certificate is admitted** when its record is not beside a deletion record naming it, no
revocation names its client key, it was not taken away and was not renewed from a certificate
taken away (§648), no retirement names its issuer, and its issuer is the authority served. Its
profiles are the record's, less every name the profile list does not hold, as today. The
admission record stays load bearing: a certificate with no record is refused.

**Two admissions that name the same certificate** are not what the plugin writes, and whoever
holds the store key can write them. The measured code let whichever it read last decide the
profiles, so the answer turned on the order of entries. What the reading does with them is S24.

**A revocation** is a record of kind `revoked` that names the client's key, in the member
`client`. It refuses every certificate whose record carries that client key, in whichever copy
and at whichever time the certificate was issued, and it never expires and is never pruned
(decision 13). The measured code also wrote the member `certificate`, and wrote a revocation for
each certificate of the client that its copy admitted (S5).

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

**What the record holds** (proposed; the kind's name, its members and its subject are S23). It
names the certificate by its admission key, the SHA-256 over the certificate that `CA.md` §217
defines, and it names the renewals made from that certificate that its writer had read (below).
The subject in its identifier is that certificate, so that a deletion record of it still tells
which certificate was taken away (§609).

**What it does in the reading** (proposed). A certificate is not admitted while a record of this
kind names it. Such a record counts as a revocation counts: as an entry wherever it sits, as an
item of any summary entry present, and as a deletion record that tells of it, whether or not a
deletion record names the entry. It is final. A client whose certificate was taken away and who
is to have access again renews with another certificate it still holds, or pairs again.

**A renewal made from a certificate that was taken away does not count** (proposed; the forms
are S29). Without that, taking a certificate away leaves a way round it (reasoning, not run):
the certificate is taken away at a gate, and whoever still holds it, the client or somebody who
took it, renews it at another gate whose copy is from before. That gate's operator sees an
ordinary renewal and approves it. The new certificate carries the same client key, the record
names only the old certificate, and after the copies meet the new certificate is still
admitted. So:

1. **An admission that a renewal writes names the certificate it was renewed from.** An
   admission that a pairing writes names none.
2. **A record that takes a certificate away names every admission renewed from that certificate
   that its writer had read**, as a record names in `after` what its writer had read (§610).
   Those are the renewals made before the certificate was taken away, in the copy where it was
   taken away, the certificate the client has just renewed to among them.
3. **An admission renewed from a certificate that was taken away counts only where the record
   that took it away names it.** An admission renewed from an admission that does not count for
   this reason does not count either, so a certificate renewed onward from it is refused as
   well. Where only a deletion record tells that a certificate was taken away, no admission
   renewed from it counts, since a deletion record names none.

This form is taken, and not the plain statement that taking a certificate away does not cover
its renewals, because it only refuses: no admission counts under it that would not count without
it (reasoning). **What it does not close**, stated plainly: a renewal that was made before the
certificate was taken away, in a copy the writer had read, stays admitted, since the record
names it. Somebody who took a certificate and renewed it before its owner gave it up holds a
certificate that works. Only the operator stops that, by taking that certificate away as well or
by revoking the client. Its cost is that an admission is refused when the admission it was
renewed from is lost and nothing tells why (S29).

**What restates it.** The summary entry holds an item for each certificate taken away, which
names the certificate and the same renewals (§611), and a record of this kind that no summary
entry present restates stops the store as any decision without its summary does (§624).

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

**A rule is a record of kind `version` that names its node.** Its members are `object`, which is
`rule:` and the node's identifier as 32 hexadecimal characters, `after` (§610), and `doc`, the
rule document. Nothing is on the node.

**Today's rule model is unchanged** (decision 9): the nearest rule wins for each profile, profiles
are combined with or, a rule can be empty, `default` answers where no rule names a profile, and
the verbs and the two switches are `CRUSDLAP`, `DESIGN.md` §136 and §137. The rule document is
today's document, as `AclDocument.Write` makes it (the design paper 4.1). The probe wrote
stand-ins for it, and how a rule taken off a node is written is S8.

**A node's rule is the versions of its object that nothing names.** A single standing version is
the rule. A newer version names every version its writer had read, whatever it holds: that is the
operator changing a rule, and it may widen. No standing version and no trace of any is a node with
no rule, which is read by where it sits, as today.

**Versions made apart give what all of them allow**, until the operator has removed versions in
the grant editor and a single version is left (§657; evaluation 6, row 22). Several gates are
supported, so more than two versions of a rule can stand, and they are combined in any order
with the same result (§610). For a single profile a rule
document is either a grant, which answers with its own verbs whatever arrives from above, or a
limit, which answers with what arrives from above cut to a set of verbs; a document that does not
name the profile and inherits is the limit that cuts nothing, and a document that does not name it
and denies is the grant of nothing. Two grants meet as the grant of the verbs both hold. A grant
and a limit, or two limits, meet as the limit to the verbs both hold (the design paper 4.3). So a
rule narrowed in a copy and taken away in another gives what the group above gives, and no more
than the narrowed rule. This was measured with the two verbs the measured code holds, read and
write, and with rules for at most two profiles (`measured-store-as-a-whole` 2.4, 3.1). Over the
eight letters and `default` the form of what all allow is the design paper's (S9).

**An object refuses** where the file shows that a version existed and holds its text nowhere: a
deletion record tells of a version of the object that no standing version names as replaced and
no summary entry in force carries, or no version stands and a version is present beside a
deletion record naming it. A node that refuses grants nothing to any profile, there and below, as
a rule document that will not read does today.

**What replaces what.** A version that a present version names is deleted with a deletion record
at the plugin's next write (§627). Every standing version is restated whole by the summary entry.

**What a node's place does.** A node under its own rule keeps it wherever it is moved: a node
with a rule granting nothing under a root that grants gave nothing in every cell of 90 where it
exists (`measured-store-as-a-whole` 3.2). A copy of a node has a new identifier, no rule names
it, and it is read under the rule of the place it lands in (§639). A node whose rule is lost in
every carrier, with no trace, inherits.

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
and §650 are read as those sections have it. None of the differences was run. What the reading
costs on KeePass's thread was not measured (S20).

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
same way (proposed).

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
   that names each key not kept, and that names in `after` every keep record its writer had
   read (§610). A keep record that another keep record names in `after` is replaced and does
   not count, under whichever key each of the two is. So a later answer replaces an earlier.
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

**What keeping a store writes** (proposed; the record's kind, its content and its item are
S23). A single write under the key kept, through the writer: a keep record, and the summary
entry, which restates it. The keep record names a key by a digest of that key, which nobody
computes who has not held that key, so a file from somebody who does not hold the database's
key cannot bring a record that names it. A keep record counts wherever it sits, as an entry, as
an item of any summary entry present and as a deletion record that tells of it. A plugin keeps
only a store that it may write (§652), and no other write is made while several keys stand.

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
   record, changes nothing. A certificate taken away and a keep record are united the same
   way, a keep record under the rules of §649. A deletion record that tells of a certificate
   taken away names no renewal, so no renewal from that certificate counts by it (§648).
3. **The versions of an object** are the records of kind `version` in force and the versions
   that the summary entries in force restate. Those that no version names stand, and the
   object's value is what all standing versions allow (§617). An object refuses as §617 states.
4. **A summary entry is in force** when it is a record and no deletion record names it.
5. **The authority served, the names on the list, the certificates admitted, each node's rule and
   each answer to a renewal** follow as §614 to §618 and §648 state.
6. **The highest format number and the highest plugin version** are the greatest that a version
   record that counts, a summary entry present, or a deletion record that tells of a version
   record states (proposed, §650). A deletion record tells of a version record when its
   identifier begins with that kind's tag under the store key. Where its subject is a format
   number up to the plugin's own, it states that number. Where it is none of those, it states a
   format number above the plugin's own, and the plugin is held back (§652). Where the subject
   is not known, the three bytes of the tag are all that tells, which the deletion record of an
   ordinary entry meets by chance about once in 17 million; what else such a deletion record is
   held to is part of S23. What the plugin does with the store follows from the comparison of
   §651, before anything is served.

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
  away, a keep record and a version record are held to the same (proposed).

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
records the page can show that a revocation, a removal or a version of a certain node was
deleted, since an identifier holds its kind and a keyed hash of its subject, and for those the
press writes the refusal down. It cannot show a decision lost in both of its carriers with no
deletion record, as a script, a merge that keeps no record or a file written over leaves it. The
page states plainly that it cannot know what was deleted without a record and that confirming
makes the shown state the state, and the tab lists what is in force, so that it can be checked
first (§636).

**What was run.** The press left the store as decided wherever it was run after a summary entry
was deleted or deduplicated (`measured-store-as-a-whole` 3.4, 3.6). It was not run after a stop
by an edited entry, and nothing of it was run as a page (§644). Pressed after a loss that left no
trace, it ends wider (§639).

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
   stopped store, which §624 rules out.
2. **The records of the act are added** to the plugin's group, and with them a version record
   where §650 has the write carry it (proposed).
3. **What a record present replaces is deleted, with a deletion record**: every version that a
   present version names; every profile addition that does not name every known removal of its
   name; every admission whose client key is revoked or whose issuer is retired; every authority
   that is retired; and every summary entry present, with every summary entry those name. A
   deletion record is written as well for a replaced record that this copy does not hold, so
   that the record stays out of force when a merge brings it. An admission whose certificate was
   taken away goes the same way (proposed, §648).
4. **A summary entry is added** (§611) over what is then in force.
5. **Every record that counts only by its history is set back** (§628).
6. **The database is saved** and the write is believed as today.

**A deletion by the plugin never takes anything away.** What step 3 deletes is already replaced,
refused or restated, and the plugin never deletes a record in order to revoke, retire, remove or
narrow.

**A write that does not land is taken back out**, as today: the entries it added are removed
with no deletion record (the design paper 10), and the entries it deleted are put back with
their deletion records removed (reasoning).

**The settings tab still buffers.** `PendingDatabase` holds records to add where it holds keys
today, OK applies them in a single turn, and Cancel drops them. The merges the tab registers
today become properties of the records (the design paper 10, not run): a removal of a name
defeats an addition that a pairing wrote underneath the dialog, since that addition did not see
it; **Allow renewal** is an answer whose `after` is what the page showed when it was pressed, so
an answer the renewal dialog wrote underneath is made apart and the refusal stands; and a
revocation is an addition that cannot fail in the buffer.

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

**What is added at the OK** is a version of the node's rule and a summary entry, which is what
the model ran. What else goes with it, and how it meets the writer's refusals, is S15.

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
- **Several gates' versions of the same rule give what all of them allow** until the operator
  has removed versions (§617).
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

**Where it lives.** While an authority is served, the authority's entry alone holds the store
key, in its protected content. After a Delete with no new authority, the retirement alone holds
it. No other place in the file and no place outside it holds the key.

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
The next Create starts a new key. Every client pairs again with the operator's approval, and
every rule, profile name and revocation written under the lost key is passed over and has to be
entered again.

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
  number (§650); a keep record under the new key that names every other key found, and names in
  `after` every keep record its writer had read (§649); and the summary entry that restates
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
the plugin's group (the kind's name, its members and its subject are S23). In this document the
words name this record alone: a record of kind `version` is something else, a version of a rule
or of an answer to a renewal (§617), and a version of an entry is its current strings or an item
of its history (§621). A version record states two things: the version of the plugin that wrote
it, as the three whole numbers the build stamps the plugin with, and the format number of the
store as that plugin writes it.

**The format number is a whole number of the store's own**, apart from the plugin's version. It
decides what a plugin does with a store (§651), and the plugin's version decides nothing: it is
there for the operator to read, on the tab and in the log, which release wrote the store and
which raised it.

**What alters the store's form**, and so moves the number (proposed):

- a new kind of record, or a new kind of item in the summary entry;
- a new member that a reader must not ignore, or a member whose meaning changes;
- a change to the check or to the identifier: what the check covers, how it is computed, what
  the identifier is made of;
- a change to what the reading takes from a record a plugin already writes, or to what a write
  deletes or restates.

A text that nothing reads does not alter it, and neither does a member that an earlier reader
may ignore without reading anything else differently. So a reader passes over a member it does
not know, in a kind it knows (proposed).

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
  states and its format number is the store's.** That record states the same format number and
  holds no plugin back. It is what keeps the record true to "the version of the plugin that
  wrote it". Whether it is worth an entry is part of S26.

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
- **Release 0.3.0 has no older store**, since the store begins with it.

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
section "Moving from 0.2" on the getting-started page. It is written by the pull request that
makes the break real, and not by this document. It names version 0.2 and the steps of the move,
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
test does not exist, and nothing of this section was run**: no route was tested against any
store. The first pull request that implements the store's guard in the API brings the test, and
by that condition no pull request puts the authority into an entry before it. The test runs
every route of the product, with every right a rule can give, against a database that holds a
composed store, with the plugin's group, records in the recycle bin and in other groups, copies
of records under other identifiers, and the reference client following references. Until that
test passes, the condition is not shown to hold, and a failure of it reopens the decision.

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
decision): a word for each reason. The words are part of the contract.

| The state | The word, proposed | Answered to |
| --- | --- | --- |
| The store is stopped (§624) | `stopped` | every request, until the store is sealed again |
| Several store keys stand (§649) | `split` | every request, until the operator keeps a store |
| The store was raised above the plugin, or holds a record of a kind the plugin does not know (§652, §621) | `outdated` | every request, until the plugin is updated or the store is started again |
| The file holds no store key (§632) | `lost` | every request that the gate can still answer (below) |
| The store waits to be raised (§652) | `frozen` | a request that needs a write into the store, and no other |

**The words are proposed, and only that each reason has its own is decided.** They are made as
today's are: `locked`, `undecided` and `busy` each name, in a single word, the state that
refuses. Like those, each is sent under `503`, in the body's `error` and in `KPG-Unavailable`,
and is described in the `Unavailable` answer of the routing document. `undecided` stays what it
is, several authorities under a single store key (§614).

**A store raised above the plugin and a record of a kind the plugin does not know are a single
reason** (proposed). To a client the two are the same state with the same remedy, and the
remedy is not the client's: the plugin on that machine is older than what wrote the store, and
has to be updated. The rule on a kind the plugin does not know is a second safeguard for the
very case the format number covers (§621), so two words would name a single cause twice. The
tab and the log tell the two apart for the operator, who is the person who can act on the
difference.

**`frozen` is the only word that not every request gets.** A gate with a newer plugin on a
store that was not raised serves, and refuses what needs a write into the store: a pairing, a
renewal and a certificate given up (§652). A read is served, and so is a change to the
operator's own entries.

**Where the words become part of the contract.** In the routing document, in the pull request
that makes the gate answer them. `API.md` §411 counts a status and its word on the wire as the
contract, and a word a client may rely on moves the minor version, which is 0.3.0. The reference
client gets a class for each word in the same pull request, as it has for each word today. A
0.2 client takes each for the gate being unavailable (§645).

**Whether every word can be sent is not settled** (S33). An answer takes a host certificate and
a gate key, and both are in the authority's record. A database with no store key holds no
authority the plugin reads, so its name resolves to nothing and the connection is dropped with
no answer, as for a database with no authority today, `DESIGN.md` §112 (reasoning). `lost` can
then be sent only where the gate still holds the gate key from before the store key was lost.
The same holds for `outdated` where the newer form changed the authority's record itself.

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
- **An X on each row, as on every row, which removes that version.**
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

**What a row is in the store, and what the editor writes, is not settled** (S32). A version in
the store is a version of a node's whole rule, for every profile (§617), and a row is a single
profile's rule. So what removing a row writes, and what the dialog's OK writes where the
operator changed another profile's rule while a group of rows still stands, follows from no
decision yet.

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
  the plugin's entries by hand; and emptying the recycle bin while it holds an entry of the
  plugin's.
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
which is the condition of §633. **Not measured**: no route was run against any store.

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

**Two gates change the same rule apart** (row 22). *What happens:* after any merge the node gives
what all versions allow, until the operator has removed versions in the grant editor (§657).
Narrower. *Measured:* on real
files (`measured-store-as-a-whole` 3.1).

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

**What the kinds that were not measured add** (reasoning; in no measured size). A certificate
taken away leaves a record and an item in every later summary entry (§648). The reference client
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
own reader has ceilings as well (S13).

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
   Owed by the first pull request that implements the guard in the API, before any authority is
   written into an entry. *Could overturn*: the authority's key in an entry.
2. **What the run of the store as decided left out** (`measured-decided-store` 8): sealing the
   store again after a stop by an edited entry; on a store with summary entries, an edit in both
   copies, an edit by pykeepass, a history item changed by a script, Database Maintenance's
   history page, and KeePassLib's other methods and the other KeePassXC releases for an edited
   entry; a rule set again after the loss of the entry that carries the key; an edit that leaves
   a record's subject intact, three copies and chains of merges; and the model with an entry's
   history, which it does not hold, so that the history rule was run on files only. Owed by the
   pull request that builds the reading. *Could overturn*: the history rule as worded, and what
   §632 states of a store started again.
3. **The record written again, beyond the run that was made** (`measured-two-readings` 5): in
   KeePass's window and by the plugin itself; beside a second KeePass with the plugin; on a
   phone or an emulator; with KeePassXC releases other than 2.8.0; with the slow key derivation;
   with a history limit other than 10 items and none; and in the searches of the design's
   model. Writing a record again as a new entry beside the edited entry was not run either, and
   is not the form decided. Owed by the pull request that builds §628. *Could overturn*:
   decisions 18 and 24.
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
12. **The reading on KeePass's thread.** The only timing is of a reading written in Python: 14 to
    16 ms after 1,000 writes (`measured-store-as-a-whole` 3.8). Owed by the pull request that
    builds the reading (S20). *Could overturn*: reading every entry of the database at every
    request.
13. **The page that seals the store again**: what it shows after each kind of loss. Nothing of it
    was run as a page.
14. **Which of KeePass's windows takes back or takes away an entry of the plugin's group** while
    the plugin writes (§626). This item is this document's own, read from `DESIGN.md` §124 and
    from no report. Owed by the pull request that moves the first write onto the store.
15. **A second store key** (§649), of which nothing was run. The case to run first is a file
    from outside that holds a retirement carrying a key of its own and naming no authority of
    the database, a rule version under that key for a node with no rule, and a summary entry
    under that key that restates both; and beside it the same file with an authority of its own
    and a retirement that names the database's authority. Each brought in by Synchronize, by
    each import method that keeps identifiers and by KeePassXC, read before the operator
    answers and after the answer. Then two copies that each made a first authority; copies that
    keep different stores and are asked again; a later answer that takes an earlier answer
    back; a lost key that comes back beside the key of a store started again; and a file from a
    holder of the database's key with a keep record that names it. Owed by the pull request
    that builds the reading. *Could overturn*: how the decision on a second key is built.
16. **A single certificate taken away** (§648), of which nothing was run: a renewal and the old
    certificate given up at a gate while another gate holds a copy from before; a renewal made
    from that certificate at the other gate in the meantime, and a renewal onward from that;
    the operator's single removal; the record deleted and edited by hand; an admission whose
    parent admission is lost; a revocation of the same client beside it; and each merge. Owed
    by the pull request that builds §648. *Could overturn*: the rule about renewals (S29).
17. **Create refused on a stopped store** (§624), which the measured code does not do. Owed by
    the pull request that builds the authority's commands on the store.
18. **A version that has to read a single way** (§607, §608): a string beginning `KPG.S.` that is
    not four digits, a line feed in a part, a member that appears twice. Each has to fail, and
    none was run. Owed by the pull request that builds the check.
19. **The format number, the version record and a kind the plugin does not know** (§647, §621),
    of which nothing was run: the highest of each after each program's merge; an older plugin
    on a store that was raised; a newer plugin on an older store, before and after the operator
    raises it; a write refused in between; a deletion record that tells of a version record; a
    record and an item of a kind the plugin does not know; a gate on a stale copy; and a store
    started again from a store that holds the plugin back, with the earlier store coming back
    by a merge. Owed by the pull request that builds §647.
20. **The move from 0.2** (§645, §654), which was read from the source of 0.2.0 and not run. The
    steps, and the path through KeePass's windows for clearing what 0.2 left, are run in a real
    KeePass by the pull request that writes the warning.
21. **The command that starts the store again** (§632), of which nothing was run: pressed on a
    whole store, by a plugin that the store's format holds back, and by a plugin that does not
    read the store's form; then the earlier store brought back by each merge, and a second gate
    that still serves the earlier store. Owed by the pull request that builds the command.
22. **The words of §656**, of which nothing was run: each state answered with its word, to a
    request before a route is chosen and to a `head`; `frozen` to a write and to no read; and
    whether `lost` and `outdated` can be sent at all (S33). Owed by the pull request that makes
    the gate answer them.
23. **The grant editor's rows for a rule in several versions** (§657). The pictures the decision
    was taken from are of a prototype with rows that were made up, and not of a store: two and
    three versions, scrolled, under a high contrast setting, and with what removing a row and
    the dialog's OK write (S32). Owed by the pull request that builds the editor on the store.

---

## 14. No migration §645

**Any 0.x release may break an earlier 0.x release** (decision 17). **The move from 0.2 to 0.3
is made by hand** (a later decision): no code in 0.3.0 reads, detects, converts or removes what
0.2 left in a file. The operator starts from scratch under 0.3, and clears what 0.2 left
afterwards.

**The rule this rests on**, which §652 cites as well. While the version begins with 0, a release
owes an earlier release nothing: no migration, no cleanup, no detection, and no reading of what
the earlier release stored. Only a release from 1.0.0 on is owed any of that by a later release.

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
`docs/`, as a short section "Moving from 0.2" on the getting-started page (§654). Both are
written by the pull request that makes the break real.

**No client program has to change for 0.3.0**, which is why it is a minor release. `API.md` §411
has a change break a client when a program that worked before has to change. Pairing again is
the operator's act and the client's ordinary command. A word that §656 adds is answered under
`503`, and a 0.2 client takes a word it does not know for the gate being unavailable (read from
the client). So a 0.2 client works with a 0.3 gate, as §411 has it. §411 also counts what a
certificate or an admission record carries: the admission changes where it is stored and what
it names (S4, S29), and that holds only while nothing of it changes what a client is sent. The
pull request that builds the admission checks it against §411.

**`CHANGELOG.md` gains its entry with the code.** The pull request that makes a 0.2 database
start over for somebody running the plugin writes that entry, marked **Breaking**, and this
document changes nothing for a person running 0.2.0.

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

**Settled, and where the answer is.**

- **S2. Where the random bytes come from.** The store key, the nonce and the second half of the
  group's identifier are drawn from the generator the product draws the gate key from today
  (read from the product).
- **S6. A certificate given up by its own client.** A record of a kind of its own, §648.
- **S9, in part. How the grant editor shows a rule in several versions.** As rows bound
  together, with no message box and no dialog held at OK, §657. What is left is below.
- **S10. A record of the recycle bin.** Not in 0.3.0, §619.
- **S15, in part. Where a rule waits between the dialog's OK and the save.** In the database, in
  memory, added at the OK, §629. What is left is below.
- **S16, in part. Which word a client gets while the gate holds back.** A word for each reason,
  §656. The words themselves are below.
- **S17. Where the guard's setting lives.** The plugin's window under Tools, §635.
- **S19. What an expired certificate left.** It stays until the compaction, and nothing is
  removed by a clock, §643.
- **S22. What the operator's documentation advises for what 0.2 left in the file.** The steps
  of §645, in the release notes and in `docs/`.
- **S25. An entry of the plugin's stripped of all its fields.** A deleted record, and not
  damage, §621.
- **S27. A 0.2 plugin on a database that holds the store.** A warning and no code, §654.
- **S28, in part. A command that starts the store again.** A command on the tab, §632. What is
  left is below.

**What the first pull request that builds the store settles.**

- **S1. The marker `v`.** The measured code wrote `2` in every record and read it nowhere. With
  the format number of §650, a marker in every record has nothing left to decide. Options: no
  such member; the member as measured, unread. Has to satisfy: no reader gives it a meaning the
  format number does not have.
- **S12. The height.** The identifier gives it two bytes, and a write adds a summary entry, so
  the format has no encoding for a height above 65,535 (read from the measured code; the longest
  run made 1,000 writes after its setup). Has to satisfy: a deletion record still tells of a
  summary entry and how high, and no write is refused for the store's own age without the
  operator having been told.
- **S13. The reader's ceilings against the store's growth.** `JsonBody` reads at most 32 members
  and a string of at most 1 MiB. The summary entry's `after` held 37,830 characters after 1,000
  writes, about 33 for each summary entry named, so it reaches that ceiling at about 31,000
  summary entries named (reasoning from the measured sizes). The items are numbered with four
  digits, which §608 requires of every item's name, so a summary entry has no name for an item
  past 9,999; it held 272 strings after 1,000 writes, and a certificate taken away adds an item
  for good (§643). Has to satisfy: the store never stops for its own growth before the
  compaction of §643 exists, or the operator is told well before it would; and a record beyond a
  ceiling is damage, never read in part, since a list cut short would leave out what it names as
  replaced or restates as refused.
- **S16. The words of §656.** Decided: a word for each reason. Proposed: `stopped`, `split`,
  `outdated`, `lost` and `frozen`, and a single word for a store raised above the plugin and for
  a record of a kind the plugin does not know. Not fixed: the words themselves, and which
  requests `frozen` is answered to beyond a pairing, a renewal and a certificate given up. Has
  to satisfy: a client can tell each state from a locked database and from several authorities;
  a 0.2 client still reads the answer as the gate being unavailable (§645); and the words are
  fixed in the routing document with 0.3.0, `API.md` §411.
- **S20. What the reading costs, and what may be kept between requests.** The reading takes
  every entry of the database with its history. The design paper keeps the checked form of an
  unchanged entry and never an answer that grants. Has to satisfy: a revocation still holds at
  the next request; a read holds KeePass's thread no longer than `DESIGN.md` §119 allows; and
  whatever is kept is keyed on the strings an entry holds and never on its modification time,
  since pykeepass 4.2.0 changes a string and leaves the time as it was
  (`measured-edited-refusals` 9.2).
- **S23. The form of the kinds that were in no run**: a certificate taken away (§648), a keep
  record (§649) and the version record (§650). For each: the name of the kind, its members, the
  subject in its identifier, and its item in the summary entry; and for the version record the
  first format number. Has to satisfy: the subject lets a deletion record of the record still
  tell what it was about (§609), which for a version record is its format number (§623); a key
  not kept is named by something nobody computes without holding that key; a certificate is
  named by its admission key, `CA.md` §217; an item is a flat JSON object of strings as every
  item is; the deletion record of an ordinary entry is not taken for a version record of a
  higher format by the three bytes of a tag alone, since that would hold the plugin back for
  good; and the version record's form is fixed with 0.3.0 for good, since every plugin from
  0.3.0 on has to read it.
- **S26. What is left of the version record** (§647). Not fixed: whether a version record is
  written where only the plugin's version rises and the format number is the store's; the form
  of the description the pinning test holds; the caption of the button that raises a store and
  the words of its warning; and whether a reader passes over a member it does not know, which
  §650 proposes. Has to satisfy: a version record never makes anything count; nothing but the
  operator's press states a higher format number in a store; and a plugin that cannot state its
  version and its format number writes nothing.
- **S31. How many bytes of the check the identifier holds.** Six, as measured (§609). They bind
  the content against a copy and against anybody without the store key, and not against
  somebody who holds the key and searches for a second content with the same six bytes. Options:
  as measured; more bytes of the check, at the cost of the subject hash or the height, which the
  identifier's sixteen bytes also hold. Has to satisfy: a deletion record still tells the kind
  and the subject of what it names; and the answer is given before 0.3.0, since an identifier's
  layout cannot change without a new format number.
- **S33. Whether every word of §656 can be sent.** An answer takes the authority's host
  certificate and the gate key. Not fixed: whether the gate still holds a gate key where the
  file holds no store key, so that `lost` reaches anybody; what an older plugin answers with
  where the newer form changed the authority's record, so that it can read no host certificate;
  and which authority's host certificate answers `split`, where `undecided` answers with each
  authority's own. Has to satisfy: a name that resolves to nothing is dropped with no answer, as
  today, `DESIGN.md` §112; and a word that turns out never to reach a client is put to the
  project, since the decision gave each reason a word.

**What a later pull request settles.**

- **S3. The authority's content.** Decided: today's authority document and the store key, in a
  single protected record. Measured: `id` and `storeKey` as hexadecimal, with no certificate
  inside. Not fixed: whether today's members `ca`, `host` and `sniKey` sit beside `storeKey` as
  they are, how `storeKey` is encoded beside the base64 of `sniKey`, and what `id` is. The
  product files an authority under the SHA-256 of its certificate, `CA.md` §204. Has to satisfy:
  a retirement names the authority by `id`, the identifier's subject is computed from it, and a
  reader refuses an `id` that is not the digest of the certificate inside, as an admission is
  refused whose stored digest disagrees with its certificate, `CA.md` §223.
- **S4. The admission's content.** Decided: today's record text. Measured: the members
  `certificate`, `client`, `issuer` and `profiles`, names separated by spaces. Today's text is a
  field to a line with names separated by commas, `CA.md` §223, and `KPG.Data` is a flat JSON
  object of strings. Options: a member for each field of today's record; a single member holding
  today's text. Has to satisfy: the reader stays total and strict as §223 has it; the anchor pin
  of `CA.md` §214 still decides which authority issued a certificate, so a record whose `issuer`
  disagrees with the pin admits nobody; and nothing of it changes what a client is sent, `API.md`
  §411.
- **S5. The revocation's content.** Decided: it names the client's key. Measured: `client` and
  `certificate`, with a revocation for each certificate of the client that the copy admitted.
  Options: as measured; a single revocation holding the client key alone. Has to satisfy: every
  certificate under that client key is refused, wherever it was issued, and the summary entry
  restates the revocation by client key.
- **S24. Two admissions that name the same certificate** (§615). The measured code let the
  admission read last decide. Options: the certificate holds the profiles that every such
  admission holds; the certificate is admitted by none of them. Has to satisfy: the answer does
  not turn on the order in which entries are read, and is never more than any single admission
  of them gives.
- **S29. A renewal from a certificate that was taken away** (§648). Proposed: an admission names
  the certificate it was renewed from, the record that takes a certificate away names the
  renewals from it that its writer had read, and a renewal it does not name does not count. Not
  fixed: the members that carry the two; and what the reading does with an admission whose
  parent admission is neither present nor taken away, as after a deletion by hand. Options for
  that: the admission does not count; it counts as an admission from a pairing does. Has to
  satisfy: the rule only refuses; the certificate a client has just renewed to is never refused
  by the old certificate being given up; and the answer does not turn on the order in which
  entries are read.
- **S7. The answer to a renewal.** Measured: an object `renew:` and the client key, with stand-in
  texts for forever and for allowed. Today the answer is about a certificate and can hold a date,
  `PAIRING.md` §312, and the design paper names the certificate and the three answers forever,
  until a moment, and allowed, of which the strictest standing answer wins. Not fixed: the
  object, the text of each answer, and how until a date is written and compared. Not fixed
  either: an answer that could not be written when it was given is written today by the next
  save of the database, whoever starts it, §312, and evaluation 4.6 does not list that write.
  Has to satisfy: two answers made apart give the stricter; an answer for this session has no
  record; no merge decides by a time.
- **S8. The rule document's text.** Decided: today's rule model, and by the design paper today's
  document as `AclDocument.Write` makes it, in the member `doc`. The probe wrote stand-ins. Not
  fixed: the text of a version that takes a rule off a node, so that the node inherits. Has to
  satisfy: it reads as no rule for every profile, and it is told apart from an empty rule, which
  grants nothing.
- **S9. What versions made apart give over the eight letters and `default`, and how the rows
  are drawn.** Decided: what all of them allow (§617), and what the operator sees (§657).
  Measured: with two verbs and at most two profiles. The design paper 4.3 gives a grant and a
  limit for each profile, has `default` deny when any standing version denies, and has the
  resolver carry a limit up its walk. Not fixed: the two switches under that combination; the
  resolver's change; and the drawing where the prototype left it weak: a bold dot that is faint,
  bold letters with less contrast on the red, a table of four rows, and a high contrast
  setting. Has to satisfy: the result is never wider than any of the versions, for every
  profile and every letter; a switch without a verb still grants nothing; and what tells the
  rows apart does not rest on color alone.
- **S11. Several groups, and keeping an authority.** What several store keys do is §649. Not
  fixed: what keeping an authority writes among several authorities under the same key, which
  the measured code did with a retirement for each authority not kept, in code that no search
  ran; and which group is written into where the tree holds several groups with the prefix. Has
  to satisfy: a deletion takes nothing away; a group that a file from outside brings is never
  where the plugin writes for that reason alone.
- **S14. The texts a person reads in KeePass's window**: a record's title, the group's notes, and
  the mark on the two entries that carry the store key, which states that the entry is not to be
  deleted. The measured title and notes are in §606 and §607. Has to satisfy: no text is covered
  by the check or read by the plugin; the mark is in place on the retirement that holds the key
  as on the authority's entry.
- **S15. What is added at a node dialog's OK, beside the rule's version** (§629). In the model a
  summary entry is added with it. Not fixed: whether the addition sets edited records back, as
  a write of the plugin's own does (§628); and how it meets the writer's refusals, since it is
  made while KeePass closes the node's own dialog. Has to satisfy: in force from the OK; no save
  started by the plugin and nothing hooked into saving; Cancel discards; and a rule narrowed
  while a save runs does not leave the file with the older rule (row 15 of §639, whose cover
  names the plugin's group written last in the file).
- **S18. Whether a string is protected.** The check covers a string's text and not its
  protection, so an edit that only takes the protection off the authority's content passes (read
  from the measured code, not run). KeePass's entry view then shows the authority's private key
  and the store key in clear to whoever looks at the window. Not fixed: whether the plugin sets
  the protection back, and when.
- **S21. A ceiling on profile names.** Today the list holds at most 32 names, which is
  `JsonBody`'s ceiling for an array. With an entry for each addition the format has no such
  ceiling. Has to satisfy: a rule document still holds at most 31 profiles, `DESIGN.md` §136.
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
names where. What stands here is what those answers newly opened.

- **S32. What does the grant editor write while a rule still shows several rows** (§657)? A
  version in the store is a version of a node's whole rule, for every profile, and a row is a
  single profile's rule. So it is open what removing a row writes where the versions also
  differ for another profile, and what the dialog's OK writes where the operator changed
  another profile's rule and left a group of rows standing. Answers: a rule's object is a node
  and a profile, so each profile's versions stand and are replaced apart, which changes the
  object the measured code wrote; the object stays the node, and the editor writes a successor
  for each version that still stands, each carrying the change; the object stays the node, and
  while a group of rows stands the editor takes no change at that node but the removal of
  versions. Has to satisfy: no question at OK and no dialog held; the versions the operator did
  not remove are left as they are; and what the gate grants is never wider than what all
  standing versions allow.
