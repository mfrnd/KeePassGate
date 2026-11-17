# Where KeePassGate keeps its data: the evaluation

What follows from the measurements on this branch: where the plugin keeps its data inside a `.kdbx`,
how that store is built, which programs a database holding it can be shared with, which sequences
still end badly, and what is left open or deferred. [`RESULTS.md`](RESULTS.md) states what was
measured and holds no recommendation; where this document cites it, it names the title of the
section. This document holds the design as the project decided it from 2026-11-15 to 2026-11-17 and
measures nothing: no probe was run for it. Where the two differ, `RESULTS.md` holds. It was written
against the product at commit `a42e38aa`, and it replaces the draft under
[`papers`](papers/README.md).

**How to read it.**

- **A reference in square brackets names where a fact was measured.** A report is named by its file
  name under `papers\ground\`, without `.md`, and its section, as [measured-store-as-a-whole 3.4]. A
  probe family is named by its directory under `spikes\storage\probes\`, as [family `crdt`], and its
  `RUN.md` there has the command and the recorded output. [model] is `spikes\storage\model`, the
  executable models of the chosen design, and [android] is `spikes\storage\android`.
- **A fact with a reference and no mark was measured**, as the place it names states it. **Read** is
  read from a program's source, from a paper or from the product, with nothing run. **Model** is the
  output of an executable model, which runs no program but itself. **Reasoning** marks what follows
  from measured facts and was not itself run. **Not measured** marks what nobody ran.
- **Wider** is a client reaching more than the last decision of the operator allowed: a revoked
  client admitted, a retired authority served, a removed profile in force, a narrowed rule widened.
  A sequence that ends wider is a **fail-open** sequence and is named as that. **Refusing** is the
  plugin refusing what the last decision allowed: for the whole database until the store is sealed
  again (section 1), or, where the file holds no store key or no authority stands, with **nothing
  served** and no press that lifts it. **Narrower** is a file granting less than the last decision
  without refusing as a whole.
- **A stale copy** is a copy of the database that has not received a decision made in another copy.
  It answers as it did before that decision, and a gate that serves it is row 8 of section 6. An
  operation that leaves a stale copy exactly as stale as it was has widened nothing, and this
  document names it as that and not as fail-open. The reports count such a copy as wider, since they
  hold every file to every decision made at any gate; where this document quotes such a count, it
  names the count as the report's. **A copy of a record** is an entry that holds a record's strings
  under an identifier other than the identifier the record's content gives, as KeePass's Duplicate
  Entry, Duplicate Group, copy and paste, and its import that gives every entry a new identifier
  make it. **A store started again** is what follows the loss of the store key: nothing is served
  until an authority is created again, and everything written under the lost key has to be entered
  again (section 4.8).
- **A gate** is a KeePass that runs the plugin and serves a database. **The gate's copy** is the
  database that KeePass holds in memory; a gate answers from that copy alone (read, from the
  product).
- **The store was run as a whole twice.** The first run [measured-store-as-a-whole] had two parts
  that are no part of the design as decided: a **repair**, which is the plugin writing back, after a
  Synchronize in its own KeePass, whatever took access away before the merge and no longer did after
  it; and a **plain stop**, which is the gate refusing the whole database for any entry of the store
  whose current version fails its check. The second run [measured-decided-store] is the store as
  decided, with neither. That report numbers the rows of section 6 as this document does. The first
  report numbers them as an earlier draft did, so this document cites its sections and never its row
  numbers. **Two later runs** were made on top of the store as decided. The first
  [measured-two-readings] ran the record written again at the plugin's next own write, which is part
  of the design as decided (section 4.3), and a reading under which a copy of a record counts, which
  is not. The second [measured-copies-extended] ran further readings of copies. No reading of a copy
  is taken (section 4.1): both reports stay on the branch as measurements of readings not taken.
- **Create, Recreate and Delete** are the KPG tab's commands for the database's certificate
  authority: Create makes the first authority or a new authority, Recreate replaces the authority
  that is served, and Delete removes it with none after it. **Create new IDs** is something else:
  the method that KeePass's File, Import preselects, which gives every arriving entry a new
  identifier. **A quick edit** is a change made from KeePass's main window without the entry dialog,
  such as setting an entry to expire now.
- **The design paper** is `options/crdt/design.md` under [`papers`](papers/README.md), and "design
  5.2" is its section 5.2. Where this document and the paper differ, section 4 names the difference,
  and this document holds.

## 1. The answer in one page

**The design, in plain words.** Everything the plugin keeps about a database is kept as ordinary
entries in a group of the plugin's own, visible and clearly named. The plugin writes each entry once
and never changes what it wrote. Taking something away, a client, a profile name, an authority, a
wider rule, is done by writing another entry, never by deleting or editing an earlier entry. Every
entry carries a check: a keyed digest of its own content, made with a secret that is kept in the
database, the **store key**, so that an entry edited by hand or planted from outside does not pass.
An entry that a person edited is read by the version the plugin wrote, which KeePass keeps in the
entry's history, and the plugin sets the entry back to that version at its next write of its own.
With every write the plugin also writes a **summary entry** (the papers call it a head) that
restates everything in force that takes access away, so that such a decision has a second carrier.
An entry counts wherever it sits in the database, the recycle bin included, and the store reads only
its own records: a copy of a record under another identifier is passed over. Nothing is kept in
custom data, nothing of the plugin's sits on the operator's own entries or groups, and no time
decides anything. The plugin's API never shows the store and refuses every write to it.

**What it protects against.**

- **Every merge by a compatible program.** With merges alone, in any order, no copy ended wider than
  what was delivered to it, in the design's model and on real files through KeePassLib's merge
  methods that match identifiers and four KeePassXC releases [measured-store-as-a-whole 2.1, 3.1;
  measured-decided-store 2.1, 2.3].
- **A single entry of the store lost or edited.** A single entry deleted in any way, recycled, or
  emptied from the bin, every deletion record removed after a single deletion, Delete Empty Groups,
  and Delete Duplicate Entries after a copy of a single decision, ended as decided, narrower or
  refusing, never wider [measured-store-as-a-whole 3.4 to 3.6]. An entry edited by hand is read by
  its earlier version, which KeePass keeps in the entry's history, and where no version passes the
  check the gate stops for the whole database. This is the **history rule**: no cell of 3,960 ended
  wider under it on a store without summary entries [measured-edited-refusals 9], and none of 590 on
  the store as decided, where ten kinds of entry were each changed by ten operations of KeePass's
  own [measured-decided-store 3.2]. The rule reaches as far as KeePass keeps the written version:
  ten edits of the same entry under KeePass's default settings. The plugin sets an edited entry back
  to the written version at its next write of its own, so the ten edits count from the plugin's last
  write. With that, none of 500 reads with a single gate ended wider, and each of 444 merge cells
  with two gates ended as the same cell with nothing written again [measured-two-readings 2.2, 2.5]
  (section 4.3).
- **A revoked client that renews at another gate.** A revocation names the client's key, so a
  certificate renewed at a gate that had not yet heard of the revocation is refused once the copies
  meet [measured-store-as-a-whole 2.4, 3.1].
- **An accident in KeePass's window.** A guard, on by default, cancels the save of the plugin's
  entries in the entry dialog and disables the commands that delete or move them or the plugin's
  group. The group dialog is not guarded. The guard is a convenience and no protection: section 4.5
  lists what it cannot reach.

**What it costs.**

- **It does not survive everything.** No store inside the file does (section 3). Section 6 has every
  sequence that still ends wider or refusing, and names which of its rows are fail-open. The
  shortest of those need a whole file written over a newer file, a file made to grant by somebody
  who can open the database and then synchronized in, two entries of the store deleted by hand
  followed by a merge that carries no deletion record, or a confirmation pressed after a loss that
  left no trace.
- **An import does not bring a copy up to date.** KeePass's File, Import preselects a method that
  gives every arriving entry a new identifier. The newer file's records then arrive as copies, which
  the store does not read. A stale copy that imports a newer file is therefore exactly as stale as
  it was before the import: nothing gets worse, and nothing is brought up to date. What can go wrong
  is an operator believing that the import updated the store. Only Synchronize brings a copy up to
  date, and the documents and the published list state that (section 6, row 6). The same import
  adds every top-level group of the operator's own a second time, with what it holds, as a new
  group at the root beside the original [family `ground-keepass`], served as the root's rule
  gives; the advice is to grant on groups and not at the root where such copies must stay unserved
  (section 6, row 10).
- **The gate stops for the whole database** when the newest summary entry is missing or an entry of
  the store has no version that passes its check, until the operator confirms what is left on the
  KPG tab (**sealing the store again**). That confirmation makes a loss permanent in that copy.
  Under KeePass's default history settings the stop comes at **the 11th edit**, which in this
  document is short for an edit that changes the content the check covers followed by ten more edits
  of the same entry of any kind, quick edits among them, with no write of the plugin's own in
  between: each such write sets the entry back, and the count starts again [measured-decided-store
  3.3; measured-two-readings 2.3]. In a database that keeps no history the stop comes at the first
  edit of the content. The KPG tab states that for such a database and the published list names it
  as a condition; nothing is refused (section 4.3). An entry whose content nobody changed passes its
  check however often it is edited otherwise (measured for a quick edit and a move, reasoning for
  other edits). Sealing the store again after such a stop was not run.
- **A lost store key is a store started again.** Two single entries hold the store key in turn: the
  authority's entry and, after the authority is deleted with no new authority, the entry that
  records its retirement. When that entry is deleted for good by hand, edited an 11th time, or has
  its content edited a single time in a database that keeps no history, the file holds no store key,
  and there is no stop to lift: nothing is served until the next Create, every client pairs again
  with the operator's approval, and every rule, profile name and revocation written before is passed
  over and has to be entered again [measured-decided-store 3.5]. By itself the loss widens nothing:
  with every earlier rule gone the store reads narrower. A client reaches more than before only
  through what the operator enters again. A narrowing rule written under the lost key is no longer
  read, so a broad rule entered again without that narrowing reaches a node the narrowing kept
  closed, and a renewal refused under the lost key is asked again. The cost is accepted for the
  deletion, the edits and the retirement alike, with the same warning and with no second carrier of
  the key (section 4.8).
- **The authority's private key is in an ordinary entry.** That is allowed only if a test of every
  API route shows the store cannot be reached, and that test has not been run (section 4.4).
- **The store grows without bound.** After 1,000 writes the summary entry held about 82 KB and named
  1,144 earlier summary entries [measured-store-as-a-whole 3.8], and the same sizes when it was run
  again as decided [measured-decided-store 4]. Release 0.3.0 ships with no pruning: the documents
  state the growth and the KPG tab shows the store's size. A compaction by hand, when the store is
  sealed again, is deferred to after 0.3.0 (sections 4.6 and 8).
- **Setting an edited entry back uses that entry's history.** Each time the plugin sets an edited
  entry back, the edited version stays in the entry's history, which takes a further item up to
  KeePass's limit (decision 24). Emptying the history instead was run as well, and no measured
  outcome depends on the choice [measured-two-readings 2.3, 2.6].

**Which programs are compatible, and under which conditions** (section 5 has the measurements).

| Program | Standing | Conditions |
| --- | --- | --- |
| KeePass 2.62 with the plugin | Compatible, the reference | Synchronize is the only way two copies are brought together: an import of a copy of the same database does not bring the store up to date. Answering "Overwrite" when KeePass reports a changed file loses the other writer's changes, and Save As over the same path writes without asking. Delete Duplicate Entries after such an import and Database Maintenance are named as unsupported. A database that keeps little or no history is named as a condition |
| KeePassXC 2.8.0 | Compatible, with conditions | It carries no deletion record, so what was deleted comes back. "Overwrite", or "Ignore" and then a save, loses the other writer's changes. Its window was measured for 2.8.0 only |
| Keepass2Android 1.15-r6 | Compatible, with conditions | "No, overwrite" is the only losing sequence found. "Always merge on conflict" removes the question and is a setting of the installation. Changes saved while the server is unreachable wait in the phone's cache until a synchronize is run. Every run was on an emulator |
| KeePassDX 4.5.8 | Incompatible, by criteria 2 and 3 | It writes over a changed file at its next save with no question that could stop it, its merge keeps no deletion record, and its merge has further measured defects. Saving alone in it keeps every place the store uses |
| KeePassDroid 2.7 | Incompatible, by criteria 2 and 3 | It writes over a changed file without asking and has no merge. Measured on an emulator |

**What is open** (sections 8 and 9). A single point of form: whether the plugin, when it sets an
edited entry back, keeps the edited version in the entry's history or empties that history. Deferred
to after 0.3.0: a compaction by hand when the store is sealed again. Not measured: the API against
the store; the guard as product code; the store's summary entries on a phone; any phone program
writing or reading a store under the history rule; an edited entry set back by the plugin itself, in
a window or beside a second KeePass with the plugin; and sealing the store again after an edit left
an entry with no version that passes.

**Where a decision and a measurement still pull against each other**, each stated in full where it
applies:

1. **The authority's key in an entry is conditional on a test that has not been run** (section 4.4).
2. **The history rule, the entry set back and the summary entries were run on files, with KeePassLib
   standing for KeePass and a probe standing for the plugin.** Not held [measured-decided-store 8;
   measured-two-readings 5]: a window, a phone, an emulator or a second KeePass with the plugin; any
   phone program or pykeepass editing a store that has summary entries; an edit of the same entry in
   both copies and a history item changed by a script on such a store; Database Maintenance's
   history page on such a store; and "Seal again", the press that seals the store again, after a
   stop by an edit. The design's model holds no history of an entry, so the history rule was run on
   files only.
3. **What a store started again reaches once rules are entered again was run in the model only.** A
   root rule set again after the loss of the entry that carries the store key was not run on files
   [measured-decided-store 8] (section 4.8; section 6, row 12).

## 2. The requirement, the criteria and the decisions

**The requirement.** A KeePassGate database is a single file that several programs write. The store
must come through that file being saved, edited, synchronized and merged by every program listed as
compatible.

**A program is compatible when measurement shows all three:**

1. its save keeps every place in the file the store uses;
2. it merges, or asks, before writing over a file that changed underneath it;
3. its merge keeps what KeePass's own Synchronize keeps, in every place the store relies on.

Which programs are compatible is a measured property, held against these criteria. A program that
loses data only when a person gives a wrong answer to its question may be listed, with that answer
stated as a condition.

**The decisions in force**, each a decision of the project. Decisions 18 to 24 were taken on
2026-11-16 and 2026-11-17, after the store as decided had been measured.

| # | Decision | What it settles, and what it costs |
| --- | --- | --- |
| 1 | **Survival is pass or fail**, for every program listed as compatible | A place in the file that a compatible program loses in a save, an edit, a synchronization or a merge cannot be the only carrier of a datum: the database's custom data and a node's custom data are closed (section 3) |
| 2 | **Nothing about a database is kept outside its `.kdbx`** on the machine a gate runs on: no file beside it, no digest, no memory of any kind that outlives KeePass. **An older file written over a newer file on purpose is accepted** | Nothing detects a rolled-back file. A person who answers a program's question so that an older copy replaces a newer file has acted on the file (section 6, row 1) |
| 3 | **One synchronized `.kdbx`, written by every device** | No second database, no inbox, no read-only phone. Every program that writes the file has to meet criterion 2 |
| 4 | **The gate's API clients take no part in keeping the database consistent** | No state held or presented by a client detects a rolled-back file |
| 5 | **The list of compatible programs is published later, from measurements. It names operations beside programs, and the KPG tab lists what the plugin's own group holds** | This document is not that list; section 5 has what it will rest on |
| 6 | **The store is the design the papers call `crdt`**: entries written once in the plugin's own group, each under a check keyed by the store key, with summary entries restating what is in force; an entry counts wherever it sits; no custom data anywhere; the API hides the store and refuses writes to it | Section 4 |
| 7 | **Damage stops the gate for the whole database, softened by the history rule.** Every record carries the check. A record counts when its current version passes, or else when a version in the entry's history passes. Only an entry of the store with no passing version, or a missing summary entry, stops the whole database, until the store is sealed again | Section 4.3 states where the rule holds and where it stops holding. For the two entries that carry the store key, no passing version leaves the file without the key, and sealing the store again does not apply: that is a store started again, which decision 19 accepts (section 4.8) |
| 8 | **The authority's private key lives in an ordinary entry, on the condition that it is safe from access through the API** | The condition is a test of every route, not yet run (section 4.4). The cost is accepted with a clear warning: that entry deleted for good by hand means every client pairs again and every rule, profile name and revocation is entered again. Decision 19 extends that acceptance to every way in which the key is lost (section 4.8) |
| 9 | **Today's rule model stays**: the nearest rule wins per profile, a rule can be empty, and `default` answers where no rule names a profile. **Access may rest on a node's place** | Section 4.9; section 6, rows 9 and 10 |
| 10 | **No plugin data on the operator's own entries or groups, and no mark on them** (the papers' mark is a reference the plugin would put on every node that carries a rule) | A copy of a node is read under the rule of the place it lands in. A node whose rule is lost in every carrier inherits (section 4.9) |
| 11 | **A rule set in a group's or an entry's dialog waits for the next save** | It is in force from the dialog's OK and is lost if KeePass ends before a save (section 6, row 21) |
| 12 | **The store key is carried over a Recreate of the authority, and kept in the retirement record when the authority is deleted with no new authority** | Rules, names and revocations stay valid across both. Whoever held a copy of the database can still make entries that pass the check (section 6, row 7) |
| 13 | **Several gates for a database are supported. A revocation names the client's key and never expires** | A gate that has not yet taken the file in still admits a client revoked elsewhere (section 6, row 8). A revocation is never pruned |
| 14 | **No repair after a synchronize.** The plugin puts nothing back after a merge; the store's own reading decides what a merge leaves | Section 4.7 has the measurement behind this |
| 15 | **No deletion record that names no entry** (the papers' witness) | Section 4.7 states what it was for and what its absence costs |
| 16 | **No hiding.** The plugin never removes its group or entries from KeePass's tree or list. **A guard, on by default, with a setting on the KPG settings to turn it off**, cancels the save of the plugin's entries in the entry dialog and disables or swallows the commands that would delete or move them, through KeePass's public members only | A convenience against accidents. The history rule and the summary entries remain the protection. The group dialog is not guarded: decision 22 (section 4.5) |
| 17 | **No compatibility with any 0.x release** | Section 10 |
| 18 | **An edited record is written again at the plugin's next own write.** Whenever the plugin writes to the store anyway, it also writes again every record that counts only by its history: the same entry's current version is set back from the history item that passes. Never after a merge, never on a read, never from a copy | The stop moves from the 11th edit since the record was written to the 11th edit since the plugin last wrote. It rests on a run with a single gate and with two gates in which no cell ended wider and no record written again undid a later write of the other gate [measured-two-readings 2]. The cost is the entry's history (section 4.3) |
| 19 | **A lost store key is accepted with the warning of decision 8**, for the entry that carries the key deleted for good, edited an 11th time, or edited a single time in a database that keeps no history, and for the retirement that holds the key as for the authority's entry. No second carrier of the key and no further dialog. The documents describe the loss as what it is: a store started again | By itself nothing widens: nothing is served until Create, every client pairs again with the operator's approval, and with every earlier rule passed over the store reads narrower. It rests on the loss as measured on real files [measured-decided-store 3.5] and on a rule set again in the model [measured-store-as-a-whole 5] (section 4.8; section 6, rows 12 and 19) |
| 20 | **A database that keeps little or no history is stated on the KPG tab and named as a condition in the published list.** Nothing is refused, and the operator's history settings are left alone | In such a database the history rule softens nothing: the first edit of a record's content stops the database or, for the entry that carries the store key, loses the key [measured-edited-refusals 9.4; measured-decided-store 3.3, 3.5] (section 4.3) |
| 21 | **Release 0.3.0 ships without pruning and states the growth**, in the documents and as the store's size on the KPG tab. **After 0.3.0: a compaction by hand when the store is sealed again**, to be designed and measured then. No automatic pruning rule is planned | It rests on the growth as measured [measured-store-as-a-whole 3.8; measured-decided-store 4] and on the design paper's cut of the lists, which ended wider in the model (sections 4.1, 4.6 and 8) |
| 22 | **The group dialog stays unguarded.** The guard covers the entry dialog and the commands, and deleting or moving the plugin's group stays blocked as a command | The group dialog tells a plugin which group it is about through a private field only [measured-hide-and-lock 3.1], and decision 16 keeps the guard to KeePass's public members (section 4.5) |
| 23 | **The store reads only its own records.** A copy of a record under another identifier is passed over: it grants nothing and takes nothing away, and the tab lists no copy as taking anything away | A stale copy that imports a newer file with new identifiers is exactly as stale as it was before the import. Only Synchronize brings it up to date, and the documents and the published list state that. Two readings under which a copy would count were measured and are not taken [measured-two-readings 3; measured-copies-extended 3 to 6] (section 4.1; section 6, row 6) |
| 24 | **When the plugin sets an edited record back, the edited version stays in the entry's history**, as an edit through KeePass's own path leaves it | Both forms were run, the edited version kept and the history emptied, and no outcome differs [measured-two-readings 2.1, 2.6]. Kept, what a person did to the entry stays visible in its history. The cost is that the history of an edited entry fills up to KeePass's limit with the plugin's rewrites and the edits between them (section 4.3) |

## 3. What the measurements leave to choose from

Each line is a fact that shapes every design.

- **A merge takes an entry's strings and attachments from a single version, whole.** Where a merge
  compares times the modification time picks the version, and whole seconds are all a file stores.
  At equal times the other version is usually kept nowhere [family `ground-keepass`; family
  `ground-keepassxc`; measured-desktop-windows 3.3]. *What follows*: nothing the plugin keeps may be
  changed in place, and no time may decide.
- **The database's custom data does not come through a KeePassXC merge** whose two loads fall in
  different seconds when the copy loaded later holds any key [family `outside-file`; family
  `hardened-custom-data`], and a group's custom data is never merged by KeePassXC [family
  `ground-keepassxc`]. *What follows*: no datum in custom data, which is where the product keeps
  everything today.
- **An entry written once and never changed** was kept by every save that was run, in every program,
  and arrived in every merge that was run for it [`RESULTS.md`, "The rules that follow";
  measured-desktop-windows 9.2; measured-source-harness 4]. What removes it is a deletion record
  that a merge applies, a program's own delete, and Delete Duplicate Entries when a copy under
  another identifier exists.
- **Deletion records travel unevenly.** KeePass carries a record only under Synchronize. KeePassXC
  applies and copies none. Keepass2Android merges as KeePass's Synchronize does. KeePassDX applies
  an arriving record and keeps none. pykeepass deletes with no record. KeePass's Database
  Maintenance removes every record at once, with no question [measured-edited-refusals 4.1;
  measured-source-harness 4; measured-desktop-windows 5.2; for pykeepass, `RESULTS.md`, "kdbxweb
  2.1.1, pykeepass 4.2.0 and KeePass 2.47"]. *What follows*: a deletion cannot carry a decision.
- **No other place in the file does better than entries.** Of the 117 places of the format that were
  listed, 113 of them measured, none is united by every merge that was run [measured-kdbx-places 1,
  7.1]. That report does not measure a whole entry as a place; what was measured of entries is the
  line above. Custom icons come closest: kdbxweb's merge deleted every icon that no node uses, and a
  maintenance command of KeePass and of KeePassXC deletes unused icons (read, not run).
- **No place was found that could serve as a memory of a single copy.** Two places were left alone
  by every merge and every save that was run, the last selected group and the last top visible
  group. By KeePass's source its window writes both at a save, and by KeePassXC's source it writes a
  zero identifier for a group that does not exist (both read, not run); the report names the two as
  candidates with those costs and did not measure the phone programs for them [measured-kdbx-places
  7.2]. *What follows* (reasoning): with decision 2, nothing in this design can tell a copy that it
  has been rolled back.
- **A field a program does not know can make it refuse the file.** An unknown field in the outer
  header makes kdbxweb and pykeepass refuse the whole file, and KeePassDX's own code compiled for
  the desktop refuses it as well, while it drops an unknown field of the inner header and opens the
  file. A header comment makes KeePassDX refuse by its source (read). On the emulator KeePassDX
  could not open a file with an unknown XML element, 2 runs [measured-kdbx-places 7.4;
  measured-source-harness 3, 6.1; measured-keepassdx 2.1, 12]. *What follows*: the store uses
  nothing but what every program writes itself.
- **A copy of a node has a new identifier and carries the node's strings** [family `rules-linked`;
  measured-desktop-windows 4.3].
- **A plugin string on an entry is undone by that entry's dialog** when the plugin writes it while
  the dialog is open: the dialog writes its own copy back at OK, with or without a change by the
  person [measured-desktop-windows 4.2]. *What follows*: a further reason for decision 10.
- **No option is free of a measured sequence that ends wider or refusing** (section 7).

## 4. The design as decided

The design is that of the paper `options/crdt/design.md`, with the differences this section names.
It was run in two executable models, in a bridge that applies its reading to real files changed
through KeePassLib 2.62 and `keepassxc-cli` [family `crdt`; model], and then as a whole twice: first
with a repair after a synchronize and a plain stop on any damaged entry [measured-store-as-a-whole],
then as decided, with neither [measured-decided-store]. A figure of the first run is quoted where
the second run found the same cell unchanged; section 4.7 names every cell that differs. The record
written again at the plugin's next own write was then run on top of the store as decided
[measured-two-readings 2]; the figures of [measured-decided-store] are of a store in which nothing
is written again for an edited record.

### 4.1 The store, part by part

| Part | What it is | What was measured of it |
| --- | --- | --- |
| The group | A single group of the plugin's own under the root, found by a fixed prefix of its identifier wherever it is, with searching and auto-type off, visible and clearly named | With those two flags off, KeePass's quick search and global auto-type never reached its entries; Find All and the Last Modified list show them [measured-desktop-windows 7; measured-hide-and-lock 2.3] |
| A record | An ordinary entry written once, with three custom strings: its kind, its content as a flat JSON object, and the check. Its notes state that it is the plugin's and that deleting it revokes nothing. Its identifier holds a keyed tag of its kind and a keyed hash of what it is about, so that a deletion record still tells what was deleted | Kept by every save and every merge that was run, the two phone programs as their own code compiled for the desktop [measured-store-as-a-whole 3.1; measured-source-harness 4] |
| The check | A keyed MAC over the entry's own `KPG.` strings under the store key. A refusal carries it as much as a grant | A hand edit of a record that takes access away wins a merge as the newer version. With no check on such records, and equally with a check whose failure is only passed over, 486 of 2,880 cells ended wider, 30 of them deletions. The check together with a stop, or with the history rule, leaves those 30 [measured-edited-refusals 5.3, 5.6] |
| The store key | A secret made with the first authority and kept in the authority's entry as a protected string. Records under another key do not pass and are passed over | Section 4.8 |
| What grants | An authority, an admission record for each client, an addition of a profile name, a rule version that names its node | Lost, an admission record or an addition fails closed: that client pairs again, that name is off the list [measured-store-as-a-whole 3.4]. The authority's entry lost takes the store key with it (section 4.8) |
| What takes away | A retirement of an authority, a revocation that names a client's key, a removal of a profile name, a narrower rule version, a standing refusal of renewals | Each is restated by every later summary entry |
| The order of decisions | A record names what its writer had seen of the same subject. Two records that do not name each other were made apart, and together they give the narrower answer | The laws of that combination held in every random pair and triple tried, and merges alone converged in every history [model; measured-store-as-a-whole 2.1] |
| The summary entry | Written with every write of the plugin: it restates every revocation, removal, retirement and standing rule version, and names every summary entry it replaces. The entry it replaces is deleted with a deletion record | Without it as a carrier, 20 of 20,000 histories failed the model's check, against 2 with it [model] |
| An entry counts wherever it sits | The reading takes every entry of the database that is a record, the recycle bin included. The design paper counted only entries directly in the group | As the paper had it, six replays on real files ended wider. With the recycle bin read for every kind of record five of them hold, and the sixth still ends wider [family `crdt`]. Reading the bin is what keeps every deletion into the bin as decided [measured-store-as-a-whole 3.4] |
| Only the store's own records count | A record of the store is an entry that passes the check under the identifier its content gives, in its current version or in a version in its history. A copy of a record, which holds the record's strings under another identifier, is passed over wherever it sits | The reading that every figure of the store as decided is of [measured-decided-store 2.1]. Two readings under which a copy counts were run and are not taken (below) |
| An entry that fails and is not the store's | An entry that carries `KPG.` strings and sits under no identifier of the store is passed over and shown, never damage | The reading every history of the model ran. KeePass's importer brings a stranger's entry with `KPG.` strings in unchanged [family `store-entries`]; that it would otherwise stop the gate is reasoning, not measured |
| Sealing the store again | The single press on the KPG tab for a stopped store. It writes a summary entry over everything that passes and is in force | Left the store as decided wherever it was run after a summary entry was deleted or deduplicated [measured-store-as-a-whole 3.4, 3.6]. Not run after a stop by an edited entry. It does not apply where the file holds no store key (section 4.8) |
| The format | KDBX 4.0, nothing above it | Both phone programs' own code, compiled for the desktop, writes an AES-KDF file with nothing above 3.1 as KDBX 3.1, entries and protection kept, and KeePassLib writes it back as 4.0 [measured-source-harness 5] |

**Where the design as decided differs from its paper.** An entry counts wherever it sits. No limit
on the lists a record or a summary entry holds (the paper cuts them to 64; a copy 65 writes behind
ended wider under the limit and is refused without it, model). A failing entry outside the store's
identifiers is passed over. A rule waits for the next save (decision 11). A revocation names a key
(decision 13). The retirement holds the store key after a Delete (decision 12). The history rule
replaces the paper's plain stop on any damaged entry (decision 7), and an edited record is written
again at the plugin's next own write (decision 18). Left out: the paper's marks on the operator's
nodes (decision 10), which are a reference the paper puts on every node that carries a rule, by
which a copy is held to its original's rule and a node whose rule is lost refuses; the witness
(decision 15), which section 4.7 explains; and the paper's press that removes the versions of nodes
that are gone, which widened on two other options' records [family `versioned-fields`; family
`rules-central`].

**The store reads only its own records** (decision 23). The store passes every copy of a record
over. A copy grants nothing, takes nothing away, is no source of the store key and is never written
again by the plugin, and the tab lists no copy as taking anything away. The API hides a copy all the
same (section 4.4).

**What that means for an import.** When a stale copy imports a newer file with Create new IDs, the
newer file's decisions arrive only as copies and are passed over. The stale copy is then exactly as
stale as it was before the import: it admits whom it admitted and gives what it gave. Nothing gets
worse. The import fails to bring the copy up to date, which only Synchronize does. What stays true
is that an operator may believe an import updated the store when it did not. The documents and the
published list therefore state: use Synchronize, not Import, to bring a copy up to date (section
5.7). In the run of the store as decided these are the 10 of 234 merge cells that the report counts
as wider [measured-decided-store 2.1]. That count holds the stale file to decisions it never held.

**Two readings under which a copy would count were measured, and neither is taken.** Both stay on
the branch as measurements of readings not taken.

- *A copy counts where its record can only take away* [measured-two-readings 3]: a revocation, a
  retirement's refusal of its authority, a profile removal, and a rule version or a renewal answer
  that refuses everything. Where a copied record needs no summary entry of the file to restate it, 5
  of the ten cells above read as the newer file's decisions give, 2 served nothing and 3 were still
  counted as wider. Where it is held, like any record, to the check that a summary entry of the file
  restates it, 9 of the ten became a stop of the whole database until the store is sealed again, and
  so did a further cell that the store as decided reads as decided [measured-two-readings 3.2].
- *A copied rule version counts as well, as a limit that can remove and never add*
  [measured-copies-extended 3]: over 396 merge cells with a rule for each profile, the 16 cells that
  the report counts as wider became none, 2 more cells read narrower than decided and 2 more served
  nothing [measured-copies-extended 3.3].
- *What either would have cost.* A limit taken from a copy outlasted the decision that replaced it
  until a Synchronize, a further import, the rule set again with the copies named, or the copy
  deleted by hand [measured-copies-extended 4.2]. Deleting the counted copies by hand gave back the
  earlier answer in all 16 histories run, and a record written from a copy left a file with no store
  key in the run that tried it [measured-copies-extended 5]. The plugin would have had to name the
  copies it read in what it writes and to list on its tab every copy that takes something away
  [measured-copies-extended 1.6].
- *Why neither is taken.* With no reading of copies, the stale copy is no worse off than before the
  import, and Synchronize brings every decision in. What the readings would have closed is an import
  failing to do what Synchronize does, and the store that reads only its own records is the simpler
  store.

### 4.2 What each datum becomes

The **authority** is an entry holding today's authority document and the store key, protected. A
**retirement** is an entry, restated by every later summary entry; it also holds the store key when
no authority follows it. A **revocation** is an entry naming the client's key, never pruned. An
**admission record** is an entry holding today's record text. The **profile list** is an entry for
each addition and each removal, and a removal wins over every addition that had not seen it. A
**standing answer to a renewal** is a version of an answer. A **rule** is a version naming its node,
and nothing is on the node. The **recycle bin** is a version naming the group that is or was the
bin, written when the plugin sees a bin at a write of its own.

**Why the authority is an entry.** In the database's custom data it was removed by a KeePassXC merge
from a stale copy that lacks it and holds another key, in 8 of 8 merges; from a stale copy whose
custom data is empty it was kept, 8 of 8 [family `outside-file`]. As an entry it is a single entry,
and it alone holds the store key while an authority is served (section 4.8).

### 4.3 The reading of an edited or missing entry: the history rule

**The rule** (decision 7). Every record carries the check. A record counts in its current version
when that version passes the check, or else in the newest version in the entry's history that
passes. An entry of the store with no passing version anywhere, or a missing summary entry, stops
the gate for the whole database until the store is sealed again.

**Where the measurement differs from that wording.** For the authority's entry and for the
retirement that holds the store key, no passing version is no stop of that kind. The file then holds
no store key, nothing is recognized as the store's, nothing is served, sealing the store again does
not apply, and the next Create starts a new key under which every earlier rule, name and revocation
is passed over [measured-decided-store 3.5, 6]. That is a store started again, which decision 19
accepts. Section 4.8 has the cases, and they are rows 12 and 19 of section 6, not row 17.

**What it is for.** A person who changes a character of a revocation in KeePass's entry dialog makes
a newer version of that entry, and the newer version wins KeePass's own Synchronize. If the reading
then ignored the edited entry, the revoked client would be admitted, with no deletion anywhere; if
it stopped the database at once, a single accidental edit would refuse every client. KeePass's entry
dialog puts the version before an edit into the entry's history, so the written version is still in
the file, and it still passes.

**Where it holds, on a store without summary entries** [measured-edited-refusals 9]: five records,
four kinds that take access away and an admission record, every merge method of KeePassLib 2.62 and
four `keepassxc-cli` releases in both directions, 3,960 merge cells beyond the first 3,708.

- **No cell ended wider** after any edit that leaves the entry in the file: a character of the
  content changed, the content string renamed or removed, every string emptied, repeated edits, an
  edit in both copies, an edit by a program that writes no history, a history item changed by a
  script. Every cell that is not unchanged is a refusal: the whole store refusing in 1,624 cells,
  and in 333 cells, where the edited record was a retirement, the answer that two authorities stand
  and none is served [measured-edited-refusals 9.3].
- **It reads on, without a stop**, while the written version is in the history: after an edit in
  KeePass's entry dialog or a quick edit, each of which adds a history item holding the whole
  earlier version [measured-hide-and-lock 4.3], and after a merge that keeps the older version as a
  history item. In the first measurement the rule left 2,840 of 2,880 cells unchanged, ended wider
  in 30 (below) and in 10 answered that two authorities stand, a retirement having been deleted with
  its record; it stopped the store in none, where a plain stop on a failing entry refused in 930,
  those 10 among them [measured-edited-refusals 5.6, 7, 9.3].
- **A reading by history with no stop is not safe.** The reading that takes a record from the
  entry's history and never stops for a failing entry ended wider in 798 of 2,160, 42 of 216, 42 of
  432 and 33 of 144 cells of four of the five parts [measured-edited-refusals 9.3]. The history rule
  itself with a failing entry read past was not run.

**Where it holds, on the store as decided** [measured-decided-store 3], with summary entries and no
repair. Ten kinds of entry: the authority, a retirement, an admission record, a revocation, a
profile addition, a profile removal, a rule version, a renewal answer, the summary entry, and the
retirement that holds the store key. Each was changed in a copy by ten operations as KeePass's own
commands make them, then read, merged with an unedited copy by KeePassLib's Synchronize and by
KeePassXC 2.8.0 in both directions, and read again after the gate's next write: 590 cells. In this
run the plugin wrote nothing again for an edited record; the record written again is measured
further below.

- **None of the 590 cells ended wider**: 390 as decided, 28 narrower, 172 refusing. Under the plain
  stop, the 500 reads before any write were 214 as decided, 8 narrower and 278 refusing. The two
  readings differ in 135 reads, where the plain stop refuses and the history rule reads as decided,
  and in no cell did the history rule give more than the plain stop.
- **Every kind read as decided while the written version was in the history**: after a single edit
  in the entry dialog, after the content string was deleted there, and after ten edits in all, in
  each copy, after either merge in both directions and after the gate's next write.
- **A move, the recycle bin and a quick edit alone** left every kind as decided under both readings.
  A quick edit changes nothing the check covers and still adds a history item, so it moves the
  written version toward the limit.
- **An edited summary entry counts in the version its history holds**, and the gate's next write
  replaces it with a new summary entry and deletes the edited entry with a deletion record.

**Where it stops holding and the whole database stops instead**, each measured
[measured-edited-refusals 9.4, 9.5, 9.7; measured-decided-store 3.3]:

- **The written version leaves the history at the 11th edit** of the same entry under KeePass's
  default limit of 10 history items: an edit that changes the content the check covers, and then ten
  more edits of any kind, ten quick edits counting as ten edits of the title do. It leaves at the
  2nd edit under a limit of a single item, and at the first edit of the content where the database
  keeps no history. From then on the whole database refuses, in that copy and in every copy the
  edited entry reaches by Synchronize or by KeePassXC 2.8.0. An entry whose content nobody changed
  passes in its current version however often it is edited otherwise: measured for a quick edit and
  a move, and reasoning from what the check covers for other edits. A write of the plugin's own that
  falls between the edits sets the entry back, and the count of edits starts again (below).
- **Database Maintenance's "Delete history entries older than (days)"** removes the written version
  when it is older than the number of days, with no question; the window offers the database's own
  value, 365 [measured-desktop-windows 5.2]. On the store this was run through KeePassLib and not
  through the window's command: a cut at 30 days removed a version 319 days old, and a cut at 365
  kept it [measured-edited-refusals 9.2, 9.4].
- **A program that writes no history**: pykeepass 4.2.0 changed the string, wrote no history item
  and kept the entry's modification time, so that both versions carry the same second at a merge. Of
  72 cells for each such edit, 36 refused.
- **A copy made by KeePass's import method Create new IDs** carries no written version, and **a
  history item edited or removed by a script** leaves none. An edit of the same entry in both copies
  did not stop the store by itself, also not in the same second: of 72 cells for each order of the
  two times, 64 were unchanged, and the 8 that refused were Create new IDs cells.
- **A merge does not always bring the written version back** from a copy nobody edited: the history
  limit applies to the merged entry, and at equal times a single version is kept. Of 504 merges of a
  revocation whose edited copy held no written version, it came out current in 96, in the history in
  89, under a new identifier in 28 and nowhere in 291. On the store as decided, a merge with an
  unedited copy brought it back in none of the cells past the limit: KeePassLib trims the merged
  history to the limit, and KeePassXC 2.8.0 kept the newer entry whole [measured-decided-store 3.3].

**The record written again at the plugin's next own write** (decision 18). Whenever the plugin
writes to the store anyway, it also writes again every record that counts only by its history: every
`KPG.` string of the history item that passes becomes current again in the same entry, under the
same identifier and in the same place. The plugin does this never after a merge, which would be the
repair that decision 14 rules out, never on a read, and never from a copy. A record whose current
version passes is not written again, and the summary entry never is, since every write replaces it.
Run on real files on top of the store as decided [measured-two-readings 1, 2]:

- **With a single gate**, ten kinds of entry by ten operations, then a write of the gate's own,
  further edits and four merges with the unedited copy: of 500 reads, 337 were as decided, 20
  narrower, 124 the whole database refusing with the store key held, 18 refusing with no key held
  and 1 with nothing served, and none wider. Every read that is not as decided follows an operation
  after which no version passes or the entry is gone [measured-two-readings 2.2].
- **Edits and writes alternating**: an entry written again after every 10 or fewer edits never
  reached the stop, over 30 edits in the longest sequence. A write with no edit before it writes
  nothing again [measured-two-readings 2.3].
- **With two gates**: the same edited record written again in both copies, and a copy writing it
  again while the other copy holds a later edit, in both orders, read as decided in each of 324
  merge cells. Where the other gate had made a decision that takes the record out, 110 of 120 merge
  cells were as decided, 4 narrower and 6 counted as wider by the report, each cell the same as with
  nothing written again; those 10 are imports with Create new IDs, in which the other gate's
  decision arrives only as a copy (section 6, row 6). **No record written again undid a later write
  of the other gate**: where the record came back into that gate's copy, the decision that had taken
  it out still held, and that gate's next write took the entry out again [measured-two-readings
  2.5].
- **What it moves, and what it does not.** The stop moves from the 11th edit since the record was
  written to the 11th edit since the plugin last wrote. The 11th edit between two writes of the
  plugin still takes the written version out of the history, and a database that keeps no history
  still loses it at the first edit of the content. Where no version passes, the store has stopped
  already and the gate cannot write, so nothing is written again; where the entry is gone, there is
  nothing to write again [measured-two-readings 2.2, 2.6].
- **What it costs: the entry's history.** Where the edited version is kept as a history item, each
  edit followed by a write adds two items, and at KeePass's limit of 10 the history of an edited
  entry of the store holds the plugin's rewrites and the edits between them and nothing older. Two
  gates reached the limit within four cycles of an edit, a Synchronize, a write at each gate and a
  Synchronize each way. Where the history is emptied instead, nothing of an edit is left after the
  next write. No measured outcome differs between the two forms, and the form built keeps the
  edited version (decision 24) [measured-two-readings 2.3, 2.5, 2.6].
- **Not measured** [measured-two-readings 5]: a window, a phone, an emulator or a second KeePass
  with the plugin; the plugin itself; KeePassXC releases other than 2.8.0; the slow key derivation;
  a history limit other than 10 items and none; and the searches of the design's model.

**A database that keeps little or no history** (decision 20). With the database's history limit at
no item, the history rule softens nothing and nothing can be written again: the first edit of a
record's content stops the database, and the first edit of the content of the entry that carries the
store key loses the key [measured-edited-refusals 9.4; measured-decided-store 3.3, 3.5]. The limit,
and the age at which Database Maintenance cuts, are the operator's own settings, and the store
relies on both. The KPG tab states this for a database whose settings keep little or no history, and
the published list names it as a condition. The plugin refuses nothing because of it and leaves the
settings alone.

**What the rule does not answer.** An entry that is gone has no history. A record deleted with its
deletion record ended wider in 30 of 2,880 cells under the history rule as under the plain stop, on
a store built without summary entries: 12 of them through KeePass's Synchronize, and 18 through
Overwrite if newer, Overwrite existing and Keep existing into the copy where the deletion was made
[measured-edited-refusals 5.6, 7, 9.3]. The summary entry is the answer to that. On the store as
decided, a revocation, a retirement, a removal, a rule version or a renewal answer deleted for good
with a record read as decided in every cell, restated by the summary entry; an admission record or a
profile addition ended narrower; the summary entry itself deleted refused the whole database; and
the authority's entry deleted left no key [measured-decided-store 3.2, 3.3;
measured-store-as-a-whole 3.4]. Both carriers deleted is row 2 of section 6.

**What is still not measured of the rule.**

- **It was run on files only.** The design's model holds no history of an entry, so an edit is
  damage there as under the plain stop [measured-decided-store 8].
- **On a store with summary entries, only Synchronize and KeePassXC 2.8.0 merged the edited copy.**
  KeePassLib's other methods and the other KeePassXC releases, an edit in both copies, an edit by
  pykeepass, a history item changed by a script and Database Maintenance's history page were run on
  the store without summary entries alone. An edit that leaves the subject of a record intact, such
  as a character of its date, three copies, and a chain of merges were run on neither
  [measured-edited-refusals 8; measured-decided-store 8].
- **Sealing the store again after a stop by an edit was not run.** It was run after a summary entry
  was deleted or deduplicated.
- **It was measured with KeePassLib standing for KeePass's dialog**, and with no phone program. What
  an edit in Keepass2Android leaves in an entry's history was not measured for a record of the
  store.

**Sealing the store again, and what its page has to state.** The press writes a summary entry over
what passes and is in force, so whatever is not on the page when the operator confirms is not in the
new summary entry. From the deletion records, the page can show that a revocation, a removal or a
version of a certain node was deleted, since an identifier holds its kind and a keyed hash of its
subject; for those the press writes the refusal down. It cannot show a decision lost in both of its
carriers with no record, as a script, a merge that keeps no record or a file written over leaves it.
The page states plainly that it cannot know what was deleted without a record and that confirming
makes the shown state the state, and the tab lists what is in force, the clients admitted, the
revocations, the profile names and the rules, so that it can be checked first. None of this was run
as a page. Pressed after a loss without a record, it ends wider (section 6, row 5). Where the file
holds no store key the press does not apply (section 4.8). A compaction of the store at this press
is deferred to after 0.3.0 (section 4.6).

### 4.4 The plugin's own API

The store is made of ordinary entries, and the product serves entries. Today no route reaches custom
data (read, from the product), so the store is out of reach by construction. In entries it is out of
reach only if the API is built to keep it there. **Nothing of this section was run**: no route was
tested against any store. By decision 8 the test of every route is a condition of the authority's
key living in an entry, and until it is run that condition is not shown to hold.

**What an entry of the store is, for the API.** An entry under an identifier of the store, which the
keyed tag lets the gate recognize, or a copy whose content passes the check. An entry is not the
store's for carrying a `KPG.` string. The store's reading passes a copy over (section 4.1); the API
hides it all the same, since a copy of the authority's entry holds the private key and the store
key.

**Absent on every route.** The plugin's group; every entry of the store, wherever it sits, the
recycle bin included; and on any other entry every field whose name begins `KPG.`. Absent means: in
no listing and no sweep, not readable, left out of the tag a client compares, not offered by the
routes that tell a client what it may do at a node, and answered like a node that is not there. The
reference client follows field references through these same routes, so it cannot follow a reference
to what they do not serve (reasoning).

**Refused on every write.** Creating, renaming, updating or stripping a field whose name begins
`KPG.`, on any entry; any change to an entry of the store or to the plugin's group; and deleting a
group that holds an entry of the store. That last refusal is the answer the product gives today for
a group that holds the hidden recycle bin (read, from the product), and like it, it tells a client
that something it cannot see is there.

**What the API cannot rule out is its own defect.** A single route that forgets the guard serves the
authority's private key and the store key to a client, which can then make a record for itself that
passes the check. In custom data a forgotten route exposes nothing. The cover is the test of every
route against a composed store (section 9), and the residue is row 13 of section 6.

**Outside the API**, the key is as visible as any protected string to whoever holds the open
database: the entry view shows a protected string masked, the entry dialog lists it masked with its
Edit and Delete buttons enabled (revealing it there was not run), and KeePass's XML export writes
protected strings in clear [measured-desktop-windows 4.3]. That is the person at the desk, whom the
product's threat model concedes.

### 4.5 In KeePass's window: no hiding, and a guard

**No hiding** (decision 16). The group and its entries stay in KeePass's tree and list. Hiding was
measured and is not taken: with the group's node and the entries removed from the window, the Last
Modified list still showed every entry of the store, a field reference in an ordinary entry still
copied a store entry's user name and password, and the status bar still named the group. Where the
removal ran while the window was idle, the status bar also still counted the hidden entries; where
it ran at the window's own update event, KeePass's thread threw, answered by the unhandled-exception
dialog of its window library [measured-hide-and-lock 2.3, 6.1]. Placing the group under the recycle
bin let Empty Recycle Bin delete the whole store with a single question [measured-hide-and-lock
2.4].

**The guard** (decision 16), on by default, with a setting on the KPG settings to turn it off. As
measured in KeePass 2.62 with a probe plugin [measured-hide-and-lock 3.2, 4.2, 6.2]:

- **The entry dialog's save is cancelled** for an entry of the store, through the dialog's public
  event that precedes a save. Enter, which presses OK, and "Yes" to KeePass's question whether to
  save the changes before closing each left the entry unchanged and the dialog open; a click on OK
  itself was not run. A disabled OK button does not do this: answered "Yes", the save question saved
  the edit anyway.
- **The commands that change a node are disabled** while a node of the store is selected, in the
  Entry and Group menus and the context menus; the toolbar's buttons are under the same switch and
  were not read back. KeePass enables them again at every update of its window, and the plugin,
  whose handler runs after KeePass's, disabled them again at each of 51 updates. A disabled
  command's shortcut did nothing; a command that was only hidden still fired from its shortcut.
- **The keys that bypass the menus are swallowed** while a node of the store is selected: Delete,
  Shift+Delete, Alt+Down (the other arrow keys were not run), the paste shortcut, and select all
  followed by Delete.
- **A drop is refused**, judged by the selection, since KeePass's drag names no entry. **Empty
  Recycle Bin is disabled** while the bin holds an entry of the store.
- **What got through in the first run of every case, and what the second run closed.** In the first
  run an entry of the store was dragged into another group, an entry was deleted for good, and a
  sort of the root's groups moved the plugin's group. With the drop refused and Empty Recycle Bin
  disabled, the second run of the drag and of Empty Recycle Bin left the file with no difference, as
  did the run of the cancelled save. The sort is in the list below. The whole set of cases was not
  run again in a single run under the final guard.

**What it rests on.** The dialog's event and the window manager's event are public. The controls and
the menu items are reached by their names in KeePass's own window through public lookup methods;
those names are not an interface, so each has to be pinned by a test. KeePass's own read-only view
of an entry is not used, since it needs a private field.

**The group dialog is not guarded** (decision 22). The group dialog tells a plugin nothing about
which group it is about except through a private field [measured-hide-and-lock 3.1], and the guard
uses KeePass's public members only. So the guard cancels no save of a group dialog, reads no private
field, and does not judge a group dialog by the selection in the main window. It covers the entry
dialog and the commands, and deleting or moving the plugin's group stays blocked as a command. What
a change made in the group dialog of the plugin's group does to the store was not run. The group is
found by its identifier and a record counts wherever it sits, so a new name or new notes lose
nothing, and the group's two flags, searching and auto-type, can be switched on there (both
reasoning).

**What the guard cannot cover** [measured-hide-and-lock 6.2; measured-desktop-windows 5.2, 6.2,
6.3]:

- XML Replace (read, not run), Database Maintenance, Delete Duplicate Entries, Delete Empty Groups
  and Import, before each of which nothing is raised that a plugin can see;
- Synchronize, which is announced to a plugin before the merge only while KeePass's trigger system
  is on, and which the guard does not stop;
- a command on a selection that holds the store without being a node of it: sorting the root's
  groups moved the plugin's group, and a delete or a duplicate of a group above the store takes the
  store with it (not run, follows from how the guard decides);
- the group dialog of the plugin's group, which is not guarded (above);
- copy and paste of an entry of the store, which makes a second entry under a new identifier;
- reading and copying every value, a key the guard does not list, a command another plugin adds, and
  every other program.

No public event precedes a delete, a move, a duplicate, a paste or a quick edit: the plugin first
sees each at the next update of the window, with the database already changed in memory. The guard
as product code, with its setting, was not run.

### 4.6 What the plugin writes, what it never does, and what grows

**What it writes, and when.**

- *A write of the plugin's own*: after an OK on its settings tab, a pairing or a renewal that the
  operator approved, or a client's revocation of itself. It goes through the single writer, is saved
  and is read back, as today (read, from the product). It adds records, deletes with a record what
  they replace, and adds the summary entry. With it, the plugin writes again every record that
  counts only by its history (section 4.3).
- *A rule decided in a node's dialog* (decision 11): in force from the dialog's OK, written by that
  person's next save. A plugin's addition made at the event that precedes a save reached the file in
  every run that tried it, for the save that ends a synchronize and for a save over a changed file
  [measured-desktop-windows 6.2]; a rule waiting in memory was run in the model only
  [measured-store-as-a-whole 1].
- *Nothing else.* It writes nothing at open or at a read, and nothing after a synchronize (decision
  14).

**What it never does.** It never changes the content of a record it wrote: the single change it
makes to an entry of the store is setting an edited entry back to the version it wrote. It never
deletes a record to take something away. It never reads a copy of a record and never writes a record
from a copy. It never puts anything on the operator's entries or groups and never writes into custom
data. It never decides by a time or by the place of a record. It keeps nothing about a database
outside the file.

**What grows** [measured-store-as-a-whole 3.8], a single gate with 10 clients, 5 names and 20 ruled
nodes. Run again under the history rule, the sizes were the same: neither the history rule nor the
absence of a repair writes anything with an operation [measured-decided-store 4]. An edited entry
grows by the history KeePass keeps, up to its limit, and an entry that the plugin set back holds its
rewrites there as well (section 4.3). The sizes below are of a store in which nothing was written
again.

| After | The summary entry: bytes of its strings, strings, earlier summary entries it names | Entries in the store | Longest single string | Deletion records | The file |
| --- | --- | --- | --- | --- | --- |
| 10 writes after the setup | 5,064; 27; 44 | 39 | 1,528 characters | 54 | 8,871 bytes |
| 100 writes | 11,951; 44; 137 | 57 | 4,598 | 222 | 16,039 |
| 1,000 writes | 81,551; 272; 1,144 | 285 | 37,830 | 2,008 | 90,167 |

The summary entry grows by an identifier with every write, since it names every summary entry it
replaces; by a restated refusal for every revocation, since none is pruned; and by the list each
standing rule version keeps of what it replaces. The deletion records grow by a record for every
replaced entry. The longest string at 1,000 writes is 13 times the longest string any run on an
emulator carried. Both phone programs' own code, compiled for the desktop, kept strings of up to
262,144 characters through load, save, edit and merge [measured-source-harness 4.1]; on a device
that was not measured.

**Release 0.3.0 ships without pruning** (decision 21). The documents state the growth, and the KPG
tab shows the store's size. No automatic pruning rule is planned: the design paper's cut of the
lists to 64 ended wider in the model (section 4.1), and the file holds no memory of which copies
exist or of what each has seen (section 3). **After 0.3.0: a compaction by hand when the store is
sealed again.** The press would then also write the store fresh and drop the lists, and a copy that
is behind would refuse until it is synchronized. That compaction is neither designed nor measured;
section 8 has what it would have to guarantee.

### 4.7 Several gates, no repair, no witness

**Several gates** (decision 13). Each gate answers from its own copy, so a gate that has not yet
taken the file in still admits a client revoked at another gate (section 6, row 8). Two gates'
versions of the same rule give what both allow until the operator settles it, and a name removed at
a gate stays removed against an addition that had not seen the removal [measured-store-as-a-whole
2.4, 3.1]. Several gates were run as files that each write; two KeePass instances with the plugin
were not run.

**A revocation names the client's key.** With a revocation that names a single certificate, 15 to 28
histories of 20,000 a cell ended wider, a renewal at a gate not yet told escaping the revocation;
with the key, the renewed certificate was refused in every merge cell but an import with new
identifiers, which brings no decision in (section 6, row 6) [measured-store-as-a-whole 2.5, 3.1].
The cost: a key has no date at which it expires, so a revocation and its restatement in every
summary entry stay for good.

**No repair after a synchronize** (decision 14). A repair was measured in the first run: after a
Synchronize at a gate, the plugin wrote again whatever took access away before the merge and no
longer did after it. With several gates it cannot tell a merge that undid a refusal from a later
decision made at another gate. It closed a widening in 1 of 480,000 histories of the search and in a
single sequence on real files (section 6, row 7). It made a file narrower than decided, writing a
rule widened, a name added again or renewals allowed again at another gate back to the earlier
state, in 8 to 74 histories of 20,000 a cell over the 24 cells of that search, and in 9 to 85 a cell
over the 24 cells of the same search with no hand loss of the authority's entry and over the ten
cells of the comparison of changes [measured-store-as-a-whole 2.6, 3.7, 6; measured-decided-store
7]. With the trigger system of KeePass off no synchronize is announced to a plugin, and an import
raises no event at all [measured-desktop-windows 6.3], so a repair would also not have run where it
was needed most.

**The store run without the repair** [measured-decided-store 2]. Every part ran twice with the same
output.

- **On real files, no cell ended wider without the repair that was not wider with it**, and none
  ended refusing that did not refuse before; every cell was held to the same cell of the first run.
  Merge cells: 221 as decided, 3 narrower and 10 counted as wider of 234 (first run: 220, 4, 10),
  each of the 10 an import with Create new IDs into a stale copy, which stays as stale as it was
  (section 6, row 6). After a deletion by hand: 126 as decided, 12 narrower, 6 refusing and none
  wider of 144, as before. After every deletion record was removed: 210 as decided of 210 (first
  run: 209, and 1 narrower).
- **The cells that differ.** A Synchronize of a copy from before a Recreate into the older copy, and
  a Synchronize after renewals were allowed again into the copy taken before: both as decided, where
  the repair had made them narrower. A single Create new IDs cell, counted as wider in both runs:
  the root now gives read and write there, where the repair had narrowed it. And 22 readings of a
  copied node, which give what the root gives, read and write (section 6, row 10), where the repair
  had written the root's earlier rule beside it.
- **Row 7 of section 6 is confirmed fail-open on real files**: with no repair, a file made by
  somebody who holds the store key ended wider at the gate and in both files.
- **Random histories on files**: 300 histories of two and three copies, no failure of the model's
  check that no file is wider than what was delivered to it, as in the first run.
- **In the model**, over the ten cells of 20,000 histories that compare the store with each change
  undone, the count of wider histories is the same with and without the repair
  [measured-store-as-a-whole 2.5]. Over the full search of 24 cells it differs by a single history
  of 480,000: 6 against 5 in the configuration with hand edits, imports and planted entries, over
  two files. In that history the authority's entry is deleted by hand in both copies, a renewal is
  refused for good in between, an authority is created and the copies are synchronized: the renewal
  is asked again, since its refusal was written under the store key that both deletions lost. The
  model's check counts that history as wider. It is the store started again of row 12 of section 6:
  the operator is asked again. The search with no hand loss of the authority's entry, the extended
  model and the exhaustive part gave the same counts with and without the repair
  [measured-decided-store 2.4].
- **What went with the repair**: the histories of merges alone that converged narrower than decided
  (35 over two files, 28 over three) and every file the repair made narrower. The rule lost before a
  save (section 6, row 21) is unchanged in every cell.

**No witness** (decision 15). The witness of the design paper is a deletion record that names no
entry, written beside a revocation and a retirement under an identifier derived with the store key,
as a third trace of the decision that no deletion of entries removes.

- *What it was for*: it is the only thing in the file that lets a store be sealed again with a
  revocation still in force after both of its entries, its own and the summary entry, were deleted
  without a deletion record. In the edited-refusals measurement, a reading that also counts the
  witness had no wider cell for a revocation, where every reading without it had at least 10
  [measured-edited-refusals 5, 7].
- *What its absence costs*: the replay in which both carriers of a revocation are deleted with no
  record, the store is sealed again and a KeePassXC merge carries the result into a stale copy ends
  with the revoked client admitted; with the witness it holds [measured-store-as-a-whole 2.3, 6].
  That is row 5 of section 6. In the search no history differed with and without it
  [measured-store-as-a-whole 2.5].
- *Why it stays dropped*: a deletion by hand followed by the confirmation is the person's own act,
  as an overwrite made on purpose is. The witness also did not travel through a KeePassXC merge from
  another database, or through a KeePassDX merge into the copy open on the phone
  [measured-desktop-windows 3.3; measured-keepassdx 12], and Database Maintenance removes it with
  every other deletion record [measured-desktop-windows 5.2].

### 4.8 The store key and its carriers

**Carried over a Recreate, kept in the retirement on a Delete** (decision 12). A copy from before a
Recreate and a copy after it merged as decided in both directions by every method but Create new
IDs, and after a Delete with no new authority followed by a later Create every rule was kept.
Without the key in the retirement, everything under the key is passed over after Delete and Create,
and a rule set again ended wider in 0 to 4 histories of 20,000 a cell [measured-store-as-a-whole
2.4, 2.5, 3.1; measured-decided-store 2.1].

**The two entries that carry the key.** While an authority is served, the authority's entry alone
holds the store key. After a Delete with no new authority, the retirement alone holds it. **Where
the entry that carries the key has no version that passes its check, the file holds no store key**:
nothing is recognized as the store's, nothing is served, the gate cannot write, sealing the store
again does not apply, and the next Create starts a new key under which every rule, name and
revocation written before is passed over and has to be entered again [measured-decided-store 3.5].
This is not the stop of section 4.3. Three things leave that entry with no passing version, each
measured on real files:

- **A deletion for good, with its deletion record.** The key is gone in the copy where the entry was
  deleted and, after Synchronize in either direction, in both copies. A KeePassXC merge carries no
  deletion record: merged into the other copy, that copy keeps the entry and reads as decided;
  merged into the copy that holds the record, the entry comes back beside its record. The
  authority's entry brought back that way gives the key again and does not stand as an authority, so
  nothing is served. **A retirement brought back that way still gives the key**, and after Create
  every cell read as decided.
- **The 11th edit of that entry under KeePass's default limit of 10 history items**, ten quick edits
  counting as ten edits in the entry dialog do, with no write of the plugin's own in between. The
  key is gone in that copy and in every copy the edited entry reaches, by Synchronize and by
  KeePassXC 2.8.0 in both directions; no merge with an unedited copy brought it back.
- **The first edit of the content in the entry dialog where the database keeps no history.**

For the authority's entry, every cell in which the key was gone served nothing. For the retirement
that holds the key, where no authority is served in any case, every cell in which the key was gone
read narrower after the next Create: the root's rule was passed over.

**The entry that carries the key, written again** [measured-two-readings 2.4]. The authority's entry
is written again like any record at the plugin's next own write, and then survives 10 further edits.
Where no version passes before a write, the gate cannot write, so nothing is written again and the
key is lost as above. For the retirement that holds the key, no authority is served, so the plugin's
next own write is Create, and that Create writes the retirement again. After it the new authority's
entry holds the same key, so a later loss of the retirement no longer loses the key. Past the limit,
Create finds no key and starts a new key.

**A lost store key is a store started again** (decision 19). Decision 8 accepted this cost for the
authority's entry deleted for good by hand, with a clear warning: the entry is marked as not to be
deleted and the tab reports a missing authority. The measurements then showed that the retirement
that holds the key is a second such entry, and that the loss needs no deletion: **the 11th edit of
either entry under KeePass's default history limit, and the first edit of the content of either
where the database keeps no history, lose the key in the same way**, and an edit reached every copy
by either merge, where a deletion through a KeePassXC merge left the other copy as decided
[measured-decided-store 3.3, 3.5]. Decision 19 accepts the loss in each of these forms, with the
same warning. The key gets no second carrier, and the loss gets no further dialog.

**What the loss is, and what it is not.** By itself it widens nothing. Nothing is served until
Create. After Create every client pairs again with the operator's approval, and every rule, name and
revocation written under the lost key is passed over, so the store reads narrower than before: in
every measured cell after the retirement's loss the root's rule was gone [measured-decided-store
3.5]. Wider applies in a single sense. A narrowing rule written under the lost key is no longer
read, so when the operator enters a broad rule again and not the narrowing, the broad rule reaches a
node that the narrowing kept closed; and a renewal that was refused under the lost key is asked
again. Nothing in the file can show the earlier narrowing [measured-store-as-a-whole 5]. A rule set
again after the loss was run in the model, and not on files on the store as decided. The documents
describe the loss in these terms: a store to be entered again in full, its narrowing rules included.
Section 6, rows 12 and 19.

**Who can make entries that pass.** The key is not renewed, so whoever held a copy of the database
with its master key can make records that pass the check, and a file from that person merged in
counts: measured on real files, wider at the gate and in both files (section 6, row 7)
[measured-decided-store 2.2]. A file from outside cannot: with planted entries, Synchronize and
Keepass2Android as the only merges, and no hand loss of the authority's entry, the search found no
failure [measured-store-as-a-whole 2.1].

### 4.9 The rule model

**Today's model stays** (decision 9). A rule names its node by the node's identifier and lives in
the store; nothing is on the node (decision 10).

- **A node under its own rule keeps it.** A node with a rule granting nothing under a root that
  grants, moved to another group, recycled, restored or deleted for good in a copy and merged by
  every method both ways, gave nothing in every cell where it exists, 90 cells
  [measured-store-as-a-whole 3.2].
- **A copy of a node is read under the rule of the place it lands in**, since a copy has a new
  identifier and no rule names it. A copy of a narrowed node beside its original under a granting
  root gave what the root gives, read and write, in every such cell of the run as decided
  [measured-store-as-a-whole 3.2; measured-decided-store 2.1]. Anybody who duplicates, copies or
  pastes a node makes such a copy, and so does an import with new identifiers: each top-level
  group of the other file arrives again, with what it holds, as a new group at the root beside
  the original [family `ground-keepass`]. The plugin cannot tell such a
  copy from a new entry: an import raises no event that a plugin can see [measured-desktop-windows
  6.3], and nothing in the file marks an entry as a copy (reasoning; no report looked for such a
  trace). The copy is served as the rule of the place it lands in gives. The documentation advises
  to grant on groups and not at the root where imported or pasted copies must stay unserved, and to
  narrow on the group and not on a single entry for what has to stay closed beside a duplicate
  (section 6, row 10).
- **A node with no rule of its own is read by where it sits**, and a merge can decide where that is
  (section 6, row 9).
- **A node whose rule is lost in every carrier inherits.** The mark of the design paper made such a
  node refuse; without it, two of the paper's replays that held fail (a press before a stale copy
  arrives, and a copy stripped of its reference) [measured-store-as-a-whole 2.3].

**What the trial of the rule models does and does not support.** A trial compared today's model with
grants that only add and with a middle form, by applying a loss to histories of an operator's edits
and counting how often the result grants what the last intent refused [measured-rollback-rule-models
6]. Each cell gives two shares of the trials made: in a sample of 1,000 histories, and in an
exhaustive world of 116,745 histories. The number of trials differs from row to row.

| Loss | Today's model | Grants only | Middle form |
| --- | --- | --- | --- |
| The whole store as it was some edits ago | 78.5% / 55.1% | 77.4% / 53.0% | 78.5% / 55.0% |
| A stale copy of the node wins, and what travels with the node goes back | 50.6% / 50.9% | 0 / 0 | 0 / 0 |
| A node's newest records lost | 50.6% / 50.9% | 10.8% / 21.9% | 8.6% / 9.1% |
| A node's rule alone goes back | 50.6% / 50.9% | 46.7% / 51.0% | 47.6% / 46.3% |
| Two edits of a node made apart: wider than what both allow | 0.5% / 0.26% | 68.1% / 50.6% | 67.0% / 33.6% |
| With no loss: intended states the model cannot give | 9.0% / 4.8% | 42.1% / 46.6% | 3.5% / 5.9% |

The trial separates the models by kind of loss and in opposite directions; where a node's rule alone
goes back it does not separate them at all. It does not show today's model to be the safer as a
whole, and it is no support for decision 9 beyond that. The middle form's figures are for the form
as the trial expressed it. It does not measure the decided store: the second row is today's model in
the product's present store, where the rule sits on the node, which the decided store ends; the
third reads today's model as versions whose deletion is not detected, where the decided store stops
(whether the trial holds a summary entry was not found in its record). No node is moved or copied in
it, no program runs in it, and it holds two copies at most [measured-rollback-rule-models 7].
Decision 9 rests on what the model can express, on two edits made apart never ending wider than
either, and on what changing the model costs the product.

**What all of this reverses in the product**, each a settled decision there: the database's own
custom data as the store; "the authority takes the file to KDBX 4.1"; a rule in the custom data of
its group or entry, with the grant editor writing into the dialog's own copy of it; the profile list
as a single key replayed as a difference; a revocation that is pruned when its certificate has
expired; and that nothing in the store is authenticated beyond the file.

## 5. How it fares, program by program

### 5.1 The three criteria

**Met** and **not met** are measured. Where a cell rests on something other than the program in its
own window or on a device, the cell names it.

| Program | 1. Its save keeps every place the store uses | 2. It merges, or asks, before writing over a changed file | 3. Its merge keeps what KeePass's Synchronize keeps, per place the store relies on | Standing |
| --- | --- | --- | --- | --- |
| KeePass 2.62 | **Met**, as KeePassLib with no window, for the store as a whole [measured-store-as-a-whole 3.1], and in the window for write-once `KPG.` entries [measured-desktop-windows 2.3] | **Met** at every save through its save command that met a changed file. Of 31 runs, 25 showed the question "Overwrite the existing file?" with Synchronize, Overwrite and Cancel, after a comparison of the file's content; 1 merged with no question under the setting that forces a synchronization; 4 had nothing to lose or no save. **Not met for Save As over the same path**: the remaining run wrote without that question [measured-desktop-windows 2.2] | The reference. Only Synchronize carries a deletion record; its other four methods carry none [family `ground-keepass`; measured-edited-refusals 4.1] | **Compatible**, under the conditions of 5.2 |
| KeePassXC 2.8.0 in its window; 2.4.2, 2.6.6, 2.7.12 and 2.8.0 as `keepassxc-cli` | **Met**, through `keepassxc-cli` in four releases [measured-store-as-a-whole 3.1; family `store-entries`] | **Met for 2.8.0**. Of 21 runs, 18 met a file changed underneath: it reloaded by itself in the run that held no unsaved change, and asked before writing in the other 17. The remaining 3 runs were merges from another database [measured-desktop-windows 3.2]. The window of the other releases: **not measured** | Entries: **met**; no `KPG.` entry of either copy was lost in 13 merges in the window, and every `keepassxc-cli` cell of the store ended as decided [measured-desktop-windows 9.2; measured-store-as-a-whole 3.1]. Deletion records: none applied and none carried, a stated condition, since criterion 3 is applied per place the store relies on | **Compatible, with the conditions** of 5.3 |
| Keepass2Android 1.15-r6, on an emulator | **Met** on the emulator for write-once `KPG.` entries [measured-android; measured-k2a-synchronize]. For the store with its summary entries: kept in 7,000 saves and 7,000 edits by the program's own code compiled for the desktop [measured-source-harness 4]; on a device **not measured** | **Met**: it asked "Merge changes?" in every run that wrote over changed content with the setting "Always merge on conflict" off, 22 runs on the emulator's storage and 11 on a WebDAV server, and with the setting on it merged without asking, 1 run. The other 10 and 9 runs of the 32 and 21 met no changed content or wrote nothing [measured-overwrite; measured-k2a-synchronize] | **Met**: KeePass's Synchronize was run on the same copies for six merges on the emulator and kept the same; the other merges on the emulator were read back with nothing missing; and all 16,000 merges of the store's records by its own code compiled for the desktop equal KeePassLib's Synchronize [measured-k2a-synchronize; measured-k2a-background 4; measured-source-harness 4] | **Compatible, with the conditions** of 5.4 |
| KeePassDX 4.5.8, on an emulator | **Met** for the places the store uses: a file opened and saved unchanged kept every carrier, the store's records included, 6 runs. Attachments can be lost at a save, and the store keeps nothing in attachments [measured-keepassdx 2.1, 12] | **Not met**: a file replaced underneath it was written over at the next save in 22 of 22 runs with a change to lose. In 9 of them it had shown a dialog, which has a single button and stops nothing [measured-keepassdx 4.1, 12] | **Not met**: section 5.5 | **Incompatible** |
| KeePassDroid 2.7, on an emulator | Met for the places the store uses; it deletes entry and group custom data, which the store does not use [measured-android] | **Not met**: it saved over a changed file without asking, 3 of 3 [measured-overwrite] | **Not met**: it has no merge | **Incompatible** |

**The store's own records on a device.** No run on a phone or an emulator carried a summary entry or
a string as long as the summary entry becomes. What ran on the emulator are write-once entries with
`KPG.` strings in a group of the plugin's own, a custom data key and deletion records; what ran the
composed store is each phone program's own database code compiled for the desktop, calibrated
against the emulator's record [measured-source-harness 2.1, 2.2]. For Keepass2Android 57 of 60
recorded cells agree, and the other 3 agree but for a usage count that differs by a single use in
three synchronize cases. For KeePassDX 33 of 34 agree: the remaining cell is a merge that ended on
the emulator and did not end in the compiled code, and 2 of the 33 agree by ending on neither side.
That figure is of the third calibration run; of the five merges with the phone's copy open, 4, 3 and
2 ended in the three runs. Code that keeps a place is a candidate that a device confirms (section
9).

### 5.2 KeePass 2.62

- **A changed file.** KeePass watches no file. At every save through its save command, at closing,
  locking and exiting, and after an entry edit where that setting is on, it compares the content
  with what it loaded and asks. **Synchronize** merged and kept everything KeePassLib's Synchronize
  keeps, the other writer's write-once `KPG.` entry among it, 3 of 3 [measured-desktop-windows 2.2,
  2.3]. A lock and an unlock loads the newer file.
- **The losing sequences**, each a condition of its listing: **Overwrite** in that dialog loses
  every change of the other writer, also when the window changed nothing; and **Save As over the
  open file's own path** loses the same with no question about the changed file
  [measured-desktop-windows 9.1]. The setting that forces a synchronization removed the question and
  merged, 1 run [measured-desktop-windows 2.2].
- **Synchronize is the only method that brings two copies together.** File, Import of a copy offers
  a choice of methods with **Create new IDs preselected**, 5 of 5 runs: every entry of the other
  copy arrives a second time under a new identifier. The store reads none of the copies of its
  records, so such an import leaves the store as it was and does not bring a stale copy up to date
  (section 6, row 6); the copies of the operator's own entries are row 10 of section 6. An import
  raises no event a plugin can see and is not saved by itself [measured-desktop-windows 5.2, 6.2].
- **Database Maintenance** removed every deletion record at once with no question, and its history
  page removed history items older than the number of days given [measured-desktop-windows 5.2]. On
  the store, removing every record after a single deletion widened nothing in 210 merges; after two
  losses it is row 4 of section 6 [measured-store-as-a-whole 3.5].
- **Delete Duplicate Entries** deletes an entry of the store that has a copy under another
  identifier: the older of the two, or whichever sits in the recycle bin. **Delete Empty Groups**
  leaves the plugin's group while it holds entries [measured-desktop-windows 5.2;
  measured-store-as-a-whole 3.6].
- **Not run**: XML Replace, which by its source can change or remove any node of the database and
  merges the result back [measured-desktop-windows 5.2].

### 5.3 KeePassXC

- **A changed file**, 2.8.0. With no unsaved change it reloads the file by itself. With an unsaved
  change it asks "Reload database", and at a save that finds the file changed it asks the same with
  Overwrite in place of Ignore. **Merge** kept every entry of both copies. **The losing sequences**:
  Overwrite; and Ignore followed by a save, which wrote with no further question
  [measured-desktop-windows 3.2, 9.2].
- **Its merges carry no deletion record.** An entry the other writer deleted comes back, and a merge
  from another database drops the other copy's records [measured-desktop-windows 3.3]. For the store
  this means: what the plugin replaced comes back and stays out of force, since its successor names
  it; an admission record or a profile addition that a person deleted by hand comes back and counts
  (section 6, row 11); and a copy that learned of a decision only through such a merge holds it in
  entries alone (section 6, rows 2 and 3).
- **At the same second the copy merged into wins** and the other version is kept nowhere. The store
  never changes an entry, so this touches the operator's own entries and not the store.
- **Not measured**: the window of 2.7.12, which was provided and not run, and of the earlier
  releases; lock, close and Save As over a changed file; its direct-write setting
  [measured-desktop-windows 8]. KeeShare and the remote synchronization of 2.8.0 were not run
  either; no report names them (not found in the record).

### 5.4 Keepass2Android 1.15-r6

- **Every run was on an emulator with Android 11**, where "the phone" below is the emulator.
- **A changed file.** It compares content and asks "Merge changes?" at every save, and at every
  synchronize while the phone holds a change of its own, that meets a changed file, also with "Check
  for modifications" off. With no change of its own a synchronize loads the changed file and asks
  nothing. **"Yes, merge"** kept everything KeePass's Synchronize keeps. **"No, overwrite" is the
  only losing sequence found**: it lost everything written since the phone's load, 18 of 18 runs on
  the phone's storage and 3 of 3 on a server [measured-overwrite; measured-k2a-synchronize]. Cancel
  wrote nothing, and the next synchronize asked again [measured-k2a-background 3].
- **"Always merge on conflict" removes the question.** With it on, every synchronization that met a
  changed file merged with nobody answering, 8 runs. In 7 of them it merged within 3 to 5 seconds:
  in front on another screen, in the background, with the screen off, and at the first unlock after
  the process was killed. In the eighth, with the server unreachable for a time, the first merge was
  seen 29 seconds after the save and the last came only after "Synchronize database..." was pressed.
  Nothing was missing in any of them; KeePass's Synchronize of the same copies was run beside that
  eighth run alone. The setting belongs to the installation: it is set on every phone and again
  after a reinstall. Opening the screen that holds it also turns on "Synchronize in background"
  [measured-k2a-background 2, 4, 8].
- **With the setting off, a background synchronization writes nothing and waits** on a question that
  cannot be dismissed, through a restart of the program, 4 of 4 runs.
- **Changes saved while the server is unreachable wait in the phone's cache**, with no message, and
  no synchronization started by itself when the server came back in the 60 seconds watched; they
  reached the server when "Synchronize database..." was run, a single run, and then merged with
  nothing missing [measured-k2a-background 4]; by the program's source the next save or open sends
  them as well (read, not run). Until then a gate does not see them.
- **A crash, once.** In 1 of 7 runs with the setting on, the program ended as the merged upload
  finished; nothing was lost [measured-k2a-background 2].
- **Format.** By the program's own code compiled for the desktop, a database with AES-KDF and
  nothing above KDBX 3.1 comes back from its save as 3.1, entries and protection kept; KeePassLib
  2.62 opens it and writes 4.0 [measured-source-harness 5].
- **Not measured**: a long time with the screen off, a kill in the middle of an upload, the periodic
  and the Wi-Fi-only options, servers other than WebDAV and SFTP, and any device but an emulator
  with Android 11 [measured-k2a-background 7]; and "Always merge on conflict" on a file on the
  phone's own storage, since the setting was measured for a remote file only [measured-overwrite 5].

### 5.5 KeePassDX 4.5.8: incompatible

- **Criterion 2.** A file replaced underneath an open KeePassDX was written over at its next save in
  22 of 22 runs with a change to lose, whatever the replacement's time or length. Its only check
  compares the file's time, reports only a time more than ten seconds later and only when a screen
  of its own resumes, and its dialog has a single button and protects nothing: 9 of 9
  [measured-keepassdx 4.1]. No wrong answer is needed, so this is no condition a list could state.
- **Criterion 3.** Its merge applies an arriving deletion record and keeps none, so a later merge
  with an older copy brings deleted entries back: in 691 of 1,000 histories of three copies run
  through its own code, entries of the store came back, admission records, profile additions and
  authorities among them [measured-source-harness 4]. At equal times the open copy wins and the
  other change exists nowhere; a group deleted on one side takes with it an entry the other side
  changed later; a move made without a new modification time is undone, while KeePassDX's own moves
  held [measured-keepassdx 12].
- **Further defects.** A merge whose arriving copy deletes nested groups can run for ever (3 of 7
  runs of a pair of copies, 2 of 2 with a surviving child group). Two attachments of the same length
  whose digests share four bytes become a single attachment at a save, and KeePass's Synchronize
  then carries the wrong bytes on. A file with an XML element it does not know cannot be opened
  [measured-keepassdx 12].
- **What holds.** Saving alone in it, with nothing changed elsewhere, keeps every place the store
  uses, and KeePassLib reads every file it wrote. Its own lock and unlock, and a new process, read
  the file afresh.

### 5.6 The rest

- **KeePassDroid 2.7: incompatible.** On an emulator it saved its older copy over a changed file
  without asking, 3 of 3, and it has no merge [measured-overwrite; measured-android].
- **kdbxweb 2.1.1, pykeepass 4.2.0, KeePass 2.47.** Each keeps entries at a save. pykeepass deletes
  with no deletion record and edits with no history item, which are the shortest ways to a loss
  without a trace and to a stop under the history rule [`RESULTS.md`, "kdbxweb 2.1.1, pykeepass
  4.2.0 and KeePass 2.47"; family `ground-others`; measured-edited-refusals 9.2]. Criterion 2 was
  measured for none of the three.
- **A file synchronizer** between two writers was not measured. Whatever replaces the file with an
  older copy has the effect of section 6, row 1.

### 5.7 The operations the list will name

By decision 5 the published list names operations beside programs. From the measurements above:

- **Supported, to bring two copies together**: KeePass's Synchronize, as a command or as the answer
  to its question at a save; KeePassXC's Merge; Keepass2Android's "Yes, merge", its "Always merge on
  conflict" and its "Synchronize database...". The list states: use Synchronize, not Import, to
  bring a copy up to date.
- **Named as losing**: every answer that overwrites (KeePass's Overwrite, KeePassXC's Overwrite and
  its Ignore followed by a save, Keepass2Android's "No, overwrite"), and KeePass's Save As over the
  open file's path.
- **Named as not bringing a copy up to date**: KeePass's File, Import of a copy of the same database
  with its preselected method, Create new IDs. The store is left as it was, and every top-level
  group of the operator's own arrives a second time, with what it holds, as a new group at the
  root [family `ground-keepass`], served as the root's rule gives. The list advises to grant on
  groups and not at the root where such copies must stay unserved.
- **Named as unsupported on a KeePassGate database**: Delete Duplicate Entries after such an import
  or after a duplicate of the plugin's entries; Database Maintenance, both its deleted-objects page
  and its history page; XML Replace; deleting, editing, moving or copying the plugin's entries by
  hand; and emptying the recycle bin while it holds an entry of the plugin's.
- **Named as a condition**: a database whose settings keep little or no history, in which a single
  edit of the content of an entry of the plugin's stops the database or, for the entry that carries
  the store key, starts the store again (decision 20).

## 6. Every sequence that still ends wider or refusing

Under the design as decided. Rows 1 to 5, 7 to 11 and 13 to 16 are fail-open: they end with a client
reaching more than the last decision allowed. Row 6 is not fail-open: it leaves a stale copy exactly
as stale as it was. Rows 12 and 19 are a store started again. Rows 17, 18 and 20 end refusing. Rows
21 to 24 end narrower or with a decision lost. Each row names where it was measured, or that it was
not. Rows 1, 5, 6, 9, 10, 12, 19 and 21 are accepted by decision; accepting a row does not make it
measured or closed. The rows keep their numbers, since the reports cite them.

| # | Sequence | Who has to do what | What happens | What covers it | Measured |
| --- | --- | --- | --- | --- | --- |
| 1 | **Fail-open.** An older copy is written whole over the newer file | A person answers Overwrite in KeePass, Overwrite or Ignore in KeePassXC, "No, overwrite" in Keepass2Android; or saves with KeePass's Save As over the same path, which asks nothing about the changed file; or uses KeePassDX, whose only dialog stops nothing, or KeePassDroid, which asks nothing; or a file synchronizer puts an older copy back | Every entry written since that program's load is gone, the summary entry with it, and nothing in the file shows it. A revocation made in between is undone | Nothing in the store, by decision 2. The conditions of section 5 | KeePass 10 runs and 1 for Save As, KeePassXC 5, Keepass2Android 21, KeePassDX 22, KeePassDroid 3 [measured-desktop-windows 9; measured-overwrite; measured-k2a-synchronize; measured-keepassdx 4.1]. The phone programs on an emulator. In the model, 34 to 244 histories of 20,000 a cell in the four cells of the search that hold an overwrite, with and without the repair [measured-store-as-a-whole 2.1; measured-decided-store 2.4]. A file synchronizer: not measured |
| 2 | **Fail-open.** A decision is deleted in both of its entries, its own and the summary entry, where it was made; then a merge carries no deletion record | A person deletes two entries of the store for good, past the guard or with it off (reasoning: no run deleted an entry with the guard as product code); then KeePassXC, or an import method of KeePass that carries no deletion record | The other copy is never told and admits the revoked client or keeps the removed name. Through Synchronize or KeePassDX the same deletions end with the whole database refusing | A Synchronize, which carries the records and stops the database | Model: replayed, and 3 histories in 24 cells of 20,000, the same with no repair [measured-store-as-a-whole 2.1, 2.4; measured-decided-store 2.4] |
| 3 | **Fail-open.** A decision is lost a second time, without a trace, in a copy that received it through a merge that carries no deletion record | The decision's entry is deleted where it was made, a KeePassXC merge delivers the summary entry alone, and that entry is deleted with no record, as pykeepass deletes | That copy is its older self and admits the revoked client, or gives a narrowed node read and write | A Synchronize with a copy that still holds the decision ended as decided | Model [measured-store-as-a-whole 2.4, 5]. Real files, with no repair: wider in the copy that removed a narrowed rule's new version and summary entry by script, as decided at the other gate [measured-decided-store 5] |
| 4 | **Fail-open.** Every deletion record is removed while a loss of both carriers stands; then a stale copy is synchronized | KeePass's Database Maintenance, which asks nothing | The older rule is back: the node gives read and write. Sealing the store again before the stale copy arrives does not help: the node inherits after the press and gives read and write after the merge | Nothing in the store. The mark of the design paper held this replay | Real files and model, the same with no repair [measured-store-as-a-whole 2.4, 3.7; measured-decided-store 5] |
| 5 | **Refusing, then fail-open on a confirmation.** A decision is lost in both of its carriers with no deletion record, and the operator seals the store again | A script, or a merge that keeps no record, removes a revocation and the summary entry; the store stops; the operator confirms | The revoked client is admitted in a stale copy after a KeePassXC merge; a node whose first rule granted nothing inherits; a refused renewal is asked again. With deletion records present, the press writes the refusal down and the result is as decided | The page states that it cannot know what was deleted without a record (section 4.3). The witness held the revocation's case and is dropped by decision 15 | Model [measured-store-as-a-whole 2.3, 2.4] |
| 6 | **A stale copy stays stale; not fail-open.** A newer copy is imported into a stale copy with Create new IDs | A person uses File, Import in KeePass on a copy of the same database and leaves the preselected method | The newer file's records arrive as copies under other identifiers, and the store reads only its own records (decision 23). The stale copy is exactly as stale as it was before the import: the revoked client as admitted, the removed name as held and the wider rule as much in force as before. Nothing gets worse. The import fails to bring the copy up to date, and an operator may believe that it did | Synchronize, which alone brings a copy up to date; the documents and the published list state: use Synchronize, not Import (section 5.7). Until then a gate on the stale copy is row 8 | Real files: 10 of 26 Create new IDs cells, each into the stale copy, the same 10 with no repair [measured-store-as-a-whole 3.1; measured-decided-store 2.1]. The reports count the 10 as wider, holding the stale file to decisions it never held. Two readings under which a copy would count were measured and are not taken (section 4.1) [measured-two-readings 3; measured-copies-extended 3]. The method preselected: 5 of 5 [measured-desktop-windows 5.2] |
| 7 | **Fail-open.** A file is merged in that was made to grant, by somebody who holds the store key | The store key is a string in an entry of the database and is never renewed (decision 12). So whoever can open the database, or could open any copy of it made since the first authority, holds it: the operator, everybody who was given the master key, and whoever kept an older copy with the master key it had then, also after the master key was changed (reasoning). That person writes, by a script and without the plugin, a rule version that passes the check and names the versions it replaces, with a summary entry; then that file and the gate's are brought together by Synchronize or by an import method that matches identifiers. A client of the API cannot do this, short of row 13, and a file from outside the database cannot | The grant counts. A node narrowed at the gate gave read and write at the gate and in both files after KeePass's Synchronize, and at the gate after File, Import with Synchronize's method, Overwrite if newer, Overwrite existing or Keep existing. Through Create new IDs the gate stayed as decided. A version made in a stale copy, which does not name the narrowing, gave the narrower of the two in the model | Nothing: the key is carried over a Recreate (decision 12), and no repair runs (decision 14). The repair had closed this sequence only where it ran and reached both files | Real files, with no repair [measured-decided-store 2.2]; with the repair, and in the model [measured-store-as-a-whole 2.4, 3.7, 5] |
| 8 | **Fail-open, for a time.** A gate answers from a copy that predates a revocation made at another gate | Two gates; the second has not synchronized since | The second gate admits the client until its KeePass takes the file in. A renewal it approves meanwhile is refused once the copies meet | A Synchronize at every gate | Model, and real files for the renewal [measured-store-as-a-whole 2.4, 3.1]. Two KeePass instances with the plugin: not measured |
| 9 | **Fail-open, by a merge's ordinary work.** A node with no rule of its own comes to sit, or stays, under a grant | No mistake is needed: KeePass's Synchronize takes the later of two moves; a KeePassXC merge of an older copy brings a deleted node back; KeePassDX undoes a move made without a new modification time | The profile reads the node | Accepted by decision 9. A rule on the node itself, which held in every cell of 90 [measured-store-as-a-whole 3.2] | On other options' records and on generic carriers [family `grant-only`; `RESULTS.md`, "A node's place, decided by a merge"]; not replayed on the store |
| 10 | **Fail-open, by anybody's copy.** A copy of a narrowed node is read under the rule of the place it lands in | Anybody who duplicates, copies or pastes a node, or an import with new identifiers | Beside its original under a group that grants, the copy gives what the group gives. The copies that an import with new identifiers makes of the operator's entries and groups land in new groups under the root and give what the root's rule gives. The plugin cannot tell a copy from a new entry (section 4.9) | Accepted by decisions 9 and 10. The advice of the documents: grant on groups and not at the root where imported or pasted copies must stay unserved, and narrow on the group for what has to stay closed beside a duplicate | Real files and model: up to 17 histories of 20,000 a cell, and 22 readings of a copied node on files with no repair [measured-store-as-a-whole 3.2, 5; measured-decided-store 2.1] |
| 11 | **Fail-open against what the person meant.** An entry of the store is deleted by hand to take a client or a name away | A person deletes an admission record as any entry is deleted, past the guard or with it off | In the bin the entry still counts. Deleted for good, the client is refused only where the deletion record reaches: a KeePassXC merge leaves the other copy admitting the client, and a merge with a stale copy brings the entry back where no record remains | The KPG tab is the only place that takes anything away; a revocation there holds | Real files [measured-store-as-a-whole 3.4]; through KeePassDX's own code compiled for the desktop, 141 admission records and 297 profile additions came back over the 6,000 merges of 1,000 histories of three copies, an entry counted for each order of merges it came back in [measured-source-harness 4] |
| 12 | **A store started again, and then entered again without an earlier narrowing.** The single entry that holds the store key is left with no version that passes, a new authority is created, and a broad rule is entered again | A person deletes for good the authority's entry or, after a Delete with no new authority, the retirement; or that entry is edited an 11th time under KeePass's default history limit with no write of the plugin's own in between, quick edits counted; or its content is edited a single time in a database that keeps no history. Then Create, and the operator sets a rule at the root | By itself the loss widens nothing. Nothing is served until Create, and sealing the store again does not apply. After Create every client pairs again with the operator's approval and everything under the lost key is passed over, so the store reads narrower: after the retirement's loss every cell read narrower, the root's rule gone. A client reaches more than before only through what is entered again: a narrowing rule written under the lost key is no longer read, so a root rule set again without that narrowing gives a node what the root gives, and a renewal refused under the lost key is asked again | Accepted by decision 19 for each of these losses, with the warning of decision 8: the entry is marked as not to be deleted, the tab reports a missing authority, and the documents describe the loss as a store to be entered again in full, its narrowing rules included. The plugin's next own write sets the entry back while a version of it still passes (section 4.8) | Real files: the loss of the key by deletion and by edits, and the narrower reading after Create [measured-decided-store 3.5]. Model: the rule set again, 3 and 7 steps [measured-store-as-a-whole 5], not run on files; and with no repair a single history more of 480,000, the renewal asked again [measured-decided-store 2.4] |
| 13 | **Fail-open.** A route of the plugin's own API serves or accepts something of the store | A defect in the product on a single route, and a client holding ordinary rights there | The client reads the authority's private key and the store key and makes a record that passes, or plants or removes a refusal (reasoning) | The test of every route, a condition of decision 8 | **Not measured**: no route was run against any store |
| 14 | **Fail-open.** A former recycle bin becomes an ordinary group | KeePass's Synchronize deciding the bin by a time; a KeePassXC merge bringing the source's bin in as a group | Clients reach what was deleted | The store's record of the bin, for a bin the plugin has seen at a write of its own | On another option's records [family `rules-marker-entry`] and on generic carriers [family `ground-keepassxc`]. On the store: not measured; no run made another group the bin [measured-edited-refusals 8] |
| 15 | **Fail-open.** A rule is narrowed while a save runs | KeePass, with the plugin's group written before the node in the file | The file holds the older rule while memory holds the narrower | The plugin's group written last; a node dialog that does not close with OK while KeePass saves | On another option's records, in a console process [family `rules-linked`]. With a rule that waits for the next save: not measured |
| 16 | **Fail-open or refusing.** Two files that hold the same entries of the store answer differently | Every fault but an overwrite, in a second model of the design | 3 pairs in 1,500 histories; the pair printed admits a client in a file and refuses it in the other | None found | Model, before the decisions of 2026-11-16: the second model of the design, under `critic` in [model]; not replayed on the store as decided |
| 17 | **Refusing.** The whole database stops | The newest summary entry deleted for good, or recycled and the bin emptied before the plugin writes again; or an entry of the store, other than the two that carry the store key, left with no version that passes: the 11th edit of the entry with no write of the plugin's own in between, quick edits counted; the first edit of the content where the database keeps no history; an edit by a program that writes no history; Database Maintenance's history page; a history item changed by a script | Every client is refused, in every copy the deletion or the edit reaches. For the authority's entry and for the retirement that holds the store key the same causes do not end here: the file is left with no key, and they are rows 19 and 12 | The plugin's next own write, which sets an edited entry back while a version of it still passes (decision 18) and can write nothing once the store has stopped. Sealing the store again, which ended as decided after a summary entry was deleted in each way and was not run after a stop by an edit. A database that keeps little or no history is stated on the tab (decision 20). A merge with an unedited copy lifted the stop in no cell past the limit; after a summary entry deleted with its record, Synchronize left both copies refusing, and a KeePassXC merge into the unedited copy left that copy as decided | Real files [measured-store-as-a-whole 3.4; measured-edited-refusals 9; measured-decided-store 3.2, 3.3; measured-two-readings 2.2, 2.3] |
| 18 | **Refusing.** Copies of the store's entries are made, then Delete Duplicate Entries | Create new IDs, or a duplicate of the plugin's group with unchanged titles, or a duplicate of the authority's entry; then Delete Duplicate Entries | The originals are deleted and the copies kept; the copies are not the store's, so no key is left and nothing is served, in that file and in a third copy synchronized with it. Sealing again does not apply, and what follows is row 19 | Named as unsupported (section 5.7) | Real files [measured-store-as-a-whole 3.3, 3.6] |
| 19 | **Nothing served, then a store started again.** The store key is lost, or the store as a whole | The authority's entry, or the plugin's group, is deleted for good by hand and the deletion is synchronized; or the authority's entry is edited an 11th time under KeePass's default history limit with no write of the plugin's own in between, quick edits counted, or its content is edited a single time in a database that keeps no history. Shift+Delete on the group asks a single question [measured-desktop-windows 5.2] | Nothing is served in any copy the deletion record or the edited entry reaches, and sealing the store again does not apply. Nothing is granted by the loss. At the next Create the store starts again under a new key: every client pairs again with the operator's approval, and every rule, name and revocation is entered again; what entering again can leave out is row 12 | Accepted by decision 19, for the deletion and for the edits alike, with the warning of decision 8. For a deletion, a copy the deletion record has not reached still holds the store; for an edit no copy helped, since the edited entry won either merge. The guard, while on, swallowed the Delete key, which moves to the bin, on the group; Shift+Delete on the group under the guard was not run | The authority's entry: real files [measured-store-as-a-whole 3.4; measured-decided-store 3.5]. The group: its deletion in the window, its reading by reasoning |
| 20 | **Refusing.** Two authorities stand, neither retired | Two gates each create an authority, or a merged file brings its own | Nothing is served until the operator chooses, as today | The chooser | Model [measured-store-as-a-whole 2.4] |
| 21 | **A rule lost after OK.** A rule set in a node's dialog is in memory until the next save | KeePass ends, or the database is closed without saving | The rule is gone, as any unsaved edit is | Saving. Accepted by decision 11 | Model: 71 to 111 histories of 20,000 a cell over the ten cells that compare the store with each change undone; 60 to 115 a cell over the 48 cells of both searches, 60 to 106 in the 24 cells of the first search and unchanged there with no repair [measured-store-as-a-whole 2.5, 5; measured-decided-store 2.4, 7] |
| 22 | **Narrower.** Two gates change the same rule apart | Any merge | The node gives what both allow until the operator settles it | Settling, in the grant editor | Real files [measured-store-as-a-whole 3.1] |
| 23 | **Narrower.** A profile is added in a copy and removed in another | Every merge that brings both copies' entries together | The removal wins over the addition that did not see it | The operator adds the name again | Model [measured-store-as-a-whole 2.4] |
| 24 | **Narrower.** An admission record or a profile addition is deleted, deduplicated, or does not arrive | A deletion by hand; Delete Duplicate Entries after a duplicate; Create new IDs into the copy that lacks it, which stays as it was (row 6) | That client is refused or that name is off the list | Pairing, or adding the name | Real files [measured-store-as-a-whole 3.1, 3.4, 3.6] |

**How far the design was run as a whole.** In the model, in the first run: twelve configurations
over two and three files, 20,000 random histories each; without a deletion by hand of the
authority's entry, 3 histories in 24 cells failed, all of them row 2, and with Synchronize and
Keepass2Android as the only merges none failed [measured-store-as-a-whole 2.1]. With no repair the
same searches gave the same counts but for a single history (section 4.7). On real files, as
decided, with the history rule and no repair: 234 merge cells over KeePassLib's five methods and
four KeePassXC releases, of which 221 ended as decided, 3 narrower and 10 are row 6, which the
report counts as wider; 144 merges after a deletion by hand, none wider; 210 merges after every
deletion record was removed, all as decided; 590 cells of an edited, moved or deleted entry of the
store, none wider; and 300 random histories with no failure of the model's check
[measured-decided-store 2, 3]. Every part ran twice with the same output. The record written again
at the plugin's next own write was run on top of that: 500 reads with a single gate, none wider, and
444 merge cells with two gates, each as the same cell with nothing written again
[measured-two-readings 2]. The model holds no history of an entry, so the history rule was run on
files only, with KeePassLib standing for KeePass's window. Nothing was run with the API, as the
plugin, with the guard, in a window or on a device; no phone program wrote or read a store under the
history rule; and sealing the store again was not pressed after a stop by an edit. A sequence that
needs what was not run is in no row.

## 7. The twelve options in short

**In** is the design taken, **in part** gives it a part or shares a part with it, **out** gives it
nothing.

| Option | Verdict | What decides it | Measured |
| --- | --- | --- | --- |
| `crdt` | **In**, with the differences of section 4.1 | The only option searched as a whole: no failure with merges alone, and the failures of section 6 with deletions and merges that carry no deletion record | [family `crdt`; model; measured-store-as-a-whole; measured-decided-store; measured-two-readings 2] |
| `store-entries` | **In part**: the carrier, which `crdt` shares | Every entry kept its strings and flags in every save. As written, with no summary entry and no check, a revocation is undone in two steps | [family `store-entries`] |
| `rules-central` | **In part**: rule versions that name their node, and what both allow for two made apart | Its prune and its repair each put an older, wider rule in force | [family `rules-central`] |
| `rules-linked` | **Out**: its part was the mark | A reference on the operator's entry was gone after an ordinary edit of that entry in another copy in 6 of 8 cells | [family `rules-linked`] |
| `wildcard` | **Out**: its part was the witness | The snapshot itself admits a revoked client in 8 cells after its newest version was deleted and merged without its record | [family `wildcard`] |
| `versioned-fields` | **Out**; its reading of a narrowing from a copy under another identifier is a reading not taken (section 4.1) | A single permanent delete closes the whole gate in 9 of 16 cells | [family `versioned-fields`] |
| `rules-on-node` | **Out** | The rule sits on the node and goes with the entry's version: a rollback followed by the loss of the newest version put the wider rule back in 6 of 8 cells under the design's own reading; a stricter reading refuses in all 8 | [family `rules-on-node`] |
| `rules-marker-entry` | **Out** | A repeated identifier leaves a later narrowing dead in 10 of 10 results | [family `rules-marker-entry`] |
| `outside-file` | **Out** | Closed by decision 2. Its keys inside the file were removed by a KeePassXC merge from a stale copy that holds another key in 8 of 8 | [family `outside-file`] |
| `hardened-custom-data` | **Out** | A KeePassXC merge whose two loads fall in the same second leaves a stale target at 7 of 14 keys, 4 merges; with the loads in different seconds the 8 merges gave all 14 | [family `hardened-custom-data`] |
| `single-entry` | **Out** | An entry changed in place is chosen by its time: the step refuses in 17 of 64 cells of KeePass without the plugin and in 10 with the plugin's memory, and a crafted file planted a rule in 5 of 6 | [family `single-entry`] |
| `grant-only` | **Out**, by decision 9 | Two grants unite to more than either copy allowed in 36 of 36 cells, and a node is kept from a profile only by its place | [family `grant-only`] |

## 8. The decisions still open

Every decision that this section listed has been taken: section 2 holds them as decisions 18 to 24,
and section 4 states each where it changes the design. No decision is open. What is left is a
single deferred design.

1. **Deferred to after 0.3.0: a compaction by hand when the store is sealed again** (decision 21).
   Release 0.3.0 ships without pruning, and no automatic pruning rule is planned. The lists grow for
   good: after 1,000 writes a summary entry of about 82 KB, whose longest string had 37,830
   characters, named 1,144 replaced summary entries, beside 2,008 deletion records
   [measured-store-as-a-whole 3.8]. The list that a rule version keeps of what it replaces grows as
   well, and so do the revocations, which decision 13 never prunes. The compaction is neither
   designed nor measured. What it would have to guarantee (reasoning): a copy that is further behind
   than the compacted store reaches ends refusing until it is synchronized, and never wider; it
   needs no knowledge of which copies exist or of what each has seen, since the file holds no memory
   of a single copy (section 3) and nothing is kept outside it (decision 2); it does not depend on a
   clock, since a clock running ahead would compact early; it holds with several gates compacting
   apart; and it leaves a store that Database Maintenance has cleared of deletion records no worse
   than today. The design paper's cut of the lists to 64 is the measured warning: for a copy 65
   writes behind, a deletion record far below the list was taken for that of a replaced summary
   entry, and the copy ended wider (model).

What waits on a measurement, and not on a decision, is in section 9. The first of it is the test of
every API route, which is the condition of decision 8.

## 9. What to measure before building

Every measurement once listed here has been made. What follows is what the reports name as not run,
in the order of what could show a decision to be wrong. What the two reports on readings of copies
name as not run is left out, since no reading of a copy is taken.

1. **The API against a composed store: the condition of decision 8.** Every route of the product,
   with every right a rule can give, against a database that holds the store: the group, entries in
   the bin and in other groups, copies under other identifiers, and the reference client following
   references. *Could overturn*: the authority's key in an entry.
2. **What the run of the store as decided left out** [measured-decided-store 8]: sealing the store
   again after a stop by an edited entry; on a store with summary entries, an edit in both copies,
   an edit by pykeepass, a history item changed by a script, Database Maintenance's history page,
   and KeePassLib's other methods and the other KeePassXC releases for an edited entry; a rule set
   again after the loss of the entry that carries the key; an edit that leaves a record's subject
   intact, three copies and chains of merges [measured-edited-refusals 8]; and the model with an
   entry's history. *Could overturn*: decision 7 as worded, and what decision 19 states of a store
   started again.
3. **The record written again, beyond the run that was made** [measured-two-readings 5]: in
   KeePass's window and by the plugin itself; beside a second KeePass with the plugin; on a phone or
   an emulator; with KeePassXC releases other than 2.8.0; with the slow key derivation; with a
   history limit other than 10 items and none; and in the searches of the design's model. Writing a
   record again as a new entry beside the edited entry was not run either, and is not the form
   decided. *Could overturn*: decisions 18 and 24.
4. **The guard as product code in KeePass's window**, with its setting: a change made in the group
   dialog of the plugin's group, which is not guarded; a delete or a duplicate of a group above the
   store; "Yes" at the save question of the dialog for several entries; paste between two databases;
   and XML Replace, which was read and never run [measured-hide-and-lock 5, 6.2;
   measured-desktop-windows 8]. *Could overturn*: nothing in the store, since the guard protects
   nothing.
5. **The store's summary entries on a device**, through Keepass2Android's save, edit and merge: a
   summary entry with a long list and a string of tens of thousands of characters
   [measured-source-harness 6.2]; and what an edit of a record of the store leaves in its history
   there, which no report measured. *Could overturn*: criteria 1 and 3 for Keepass2Android.
6. **A rule that waits for the next save**, on files and in the window: the dialog's OK while a save
   runs, a save that is answered with Synchronize and so saves twice, and KeePass ending before a
   save [measured-store-as-a-whole 7]. *Could overturn*: decision 11 as worded.
7. **Two KeePass instances with the plugin** on a single synchronized file: a revocation at the
   first, a renewal at the second, a rule changed at both. Several gates were run as files that each
   write [measured-store-as-a-whole 7].
8. **KeePassXC's window in the releases other than 2.8.0**, and in 2.8.0 its lock, close and Save As
   over a changed file and its direct-write setting [measured-desktop-windows 8]. *Could overturn*:
   which releases the list names.
9. **Keepass2Android beyond the runs made**: a long time with the screen off, a kill in the middle
   of an upload, the periodic and Wi-Fi-only options, servers other than WebDAV and SFTP, and a
   device that is no emulator [measured-k2a-background 7].
10. **A real file synchronizer** between two writers, with a conflict made on purpose. *Could
    overturn*: what the list has to state beside row 1 of section 6.
11. **The rows of section 6 that were not replayed on the store**: a node's place decided by a
    merge, a merge that makes another group the recycle bin, a rule narrowed while a save runs, the
    differing files of row 16, and a deleting device whose clock is behind, on real files
    [measured-store-as-a-whole 7].
12. **The reading on KeePass's own thread.** The only timing is of a reading written in Python: 14
    to 16 ms after 1,000 writes [measured-store-as-a-whole 3.8]. *Could overturn*: reading every
    entry of the database on every request.
13. **The page that seals the store again**: what it shows after each kind of loss. Nothing of it
    was run as a page.

## 10. Building it

A plan is not part of this document. What is agreed:

- **0.2.0 was released on 2026-11-17, with today's store.**
- **The new store is 0.3.0.** It ships without pruning and states the growth (decision 21).
- **No migration**: any 0.x release may break an earlier 0.x release (decision 17), so nothing
  converts, detects or refuses what an earlier release wrote.
- **Nothing merged for 0.3.0 refers to the old store**, but for the design documents, `CHANGELOG.md`
  and `CLAUDE.md`.
- **After 0.3.0**: the compaction by hand when the store is sealed again, to be designed and
  measured then (section 8).
