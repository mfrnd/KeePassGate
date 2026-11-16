# Critique of option `store-entries`: security, KeePass's own behavior, the operator, the cost

Written 2026-11-15 against KeePassGate master `a42e38aa`. Key: `store-entries-security-and-fit`. It
attacks `option-store-entries.md` ("The database-level data as ordinary entries in a dedicated
group") through four lenses: the caller in the threat model and an attacker-supplied file; where the
authority's private key now shows up; what KeePass's own functions and the present guards do to a
store made of entries; what the operator sees and can do wrong; and what the change costs in this
code base. It does not repeat the design's own section 11 except where that section understates a
problem. This is a defensive review: each weakness is paired with the change that closes it.

## How statements are marked

- **[M here]**: measured for this critique, from a console, with invented content. Scripts and
  output are under
  `spikes\storage\probes\store-entries\security-and-fit\`:
  `scripts\import-plant.ps1` with `out\import-plant.txt`, `scripts\pk_delete.py` with
  `out\pk-delete.txt`, and `affected-tests.txt`.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M store-entries]**: measured
  in that ground report, by `spikes/revocation-merge`, or by the design's own runs.
- **[R file:line]**: read from source. KeePass 2.62 is `KeePass-2.62-Source.zip` as the KeePass
  ground report extracted it, paths from the archive root. KeePassDX 4.5.8 is commit `77ecfa8654a5`
  as the Android ground report cloned it, paths from
  `database/src/main/java/com/kunzisoft/keepass/database/`. KeePassGate is `a42e38aa` in
  the product at `a42e38aa`.
- **[A]**: assumed. Neither run nor read.

## The verdict

**Viable with changes.** The design's foundations hold where I could check them: no route moves a
node or lets a caller choose an identifier, a field reference cannot reach a custom string's
content, a group's tag is computed from the group alone and not from what is inside it, no KeePass
dialog copies and writes back a group's list of entries, and KeePass picks KDBX 4.0 for everything
the store uses (the section "What was checked and holds"). But four problems are serious. Each has a
named change, so none is fatal: no essential requirement is beyond this option because of what it
is.

| # | Rating | Problem |
| --- | --- | --- |
| S1 | serious | A refusing record is believed by its content wherever it is found, and the prune then deletes for good what it refuses: an entry carrying two strings can retire the authority |
| S2 | serious | A granting record moved to the recycle bin is treated as "admitted again" when it returns, and a KeePassDX merge returns it with no person acting |
| S3 | serious | The authority's private key moves from a place no ordinary screen shows to one copy, print or export away in every program, and the key alone has value |
| S4 | serious | The refusing records are durable, visible clutter that only grows; removing them is one gesture that undoes a revocation or a retirement from a stale copy |
| M1 | minor | Every request, the cheapest included, walks the whole tree on KeePass's thread |
| M2 | minor | "Searching off" does not keep the store out of KeePass's all-entries view, its Find-all, or `{REF:}` resolution |
| M3 | minor | Two healthy profile lists can merge to more than 32 names, which the design reads as no list and which shuts every client out |
| M4 | minor | A database that ever ran master or 0.1.0 stays KDBX 4.1 and keeps the old authority key in plain custom data |
| M5 | minor | The `KPG.` absence guard hides entries a client already made, and `409` still tells a caller one is there |
| M6 | minor | "Written once, never changed" is the plugin's discipline, not a property the file enforces; two design claims lean on it |
| M7 | minor | Smaller write-rule gaps |
| M8 | minor | The design's one line on tests understates the rewrite |

## What was checked and holds

So the design gets credit for what it got right, and so these are not re-raised as problems.

1. **No route moves a node, and none lets a caller choose an identifier.** `updateEntry` and
   `updateGroup` declare only `Name`, `Notes`, the standard fields and `expires`; there is no
   `parent` or `group` member and no move route [R src/KeePassGate/Spec/openapi.yaml:811-834,
   :1216-1232, :1982-2030]. A created node gets a fresh random UUID
   [R src/KeePassGate/Bridge/KdbxVault.cs:385, :603]. So guard point 3's defense of granting records
   (their identifiers are derived and the API cannot name them) rests on something true today.
2. **A field reference cannot read a store value.** `{REF:}` searches fields and returns the first
   match's own standard field or its UUID [R KeePass/Util/Spr/SprEngine.cs:583-606, :650]; it
   ignores the searching switch [R :650], which is the hazard the inventory flagged, but it can
   surface only a standard field or the UUID, never a custom string, so `KPG.Data` and `KPG.Kind`
   are unreachable through it. The store's values are custom strings, so a reference in a user entry
   cannot exfiltrate the authority. (It can match a store entry and return that entry's empty
   `Title`/`UserName`, which is harmless.)
3. **A group's tag does not move with its contents.** `NodeTags.Of(PwGroup)` hashes the group's
   UUID, parent, times, name and notes, and nothing about its entries
   [R src/KeePassGate/Bridge/NodeTags.cs:354-372]. So putting the store under the root creates no
   ETag oracle by which a client could watch pairings or revocations land.
4. **No KeePass dialog copies a group's entry list.** The group dialog copies the group's own
   properties and custom data, the entry dialog one entry's strings, maintenance the database custom
   data [R keepass 4]. None holds a snapshot of a group's child entries, so an added store entry
   survives every dialog's OK, which is the design's load-bearing claim in section 5.
5. **KDBX 4.0 suffices.** `GetMinKdbxVersion` returns 4.1 only for a group or entry tag, an entry
   excluded from reports, a named or timed custom icon, or a per-key custom-data time
   [R KeePassLib/Serialization/KdbxFile.cs:351-396]; the store uses none, so dropping to 4.0 is
   real, and the KeeWeb-unreadable class (timestamped custom data in a 4.1 file) is left behind for
   a fresh database [M others 5].

## Problems

### S1. A planted refusal is believed, and the prune makes it permanent (serious)

The read pass, step 3, counts "an entry anywhere that carries `KPG.Kind` of a refusing kind and
content naming a subject ... whatever its UUID", so a copy an import renumbered still refuses. The
prune (section 5) then permanently deletes, with a deletion record, "records that came back beside a
revocation" and "authority entries beside their retirement". Put together, two strings placed in any
entry of the file turn into the permanent loss of whatever they name.

**The mechanism.** The authority's digest is not secret: every client keeps `authority.pem` in its
credentials directory [R src/pykeepassgate/credentials.py:68, :479], and a client learns its own
profile names from `GET /gate/whoami`. A retirement's subject is that digest; a profile removal's
subject is `<name>/<generation>`. So the content of a refusing record about the open database is
derivable by a legitimate client, which is exactly the actor the threat model names ("a legitimate
client asking for more than it should").

**The carrier.** The record need not arrive as a KDBX merge. KeePass 2.62's own importers write a
file's custom fields into entries by their own names, verbatim. Measured: a Bitwarden JSON file
whose one item carried `KPG.Kind = retired` and `KPG.Data = {"v":"1","authority":"<64 hex>"}`, run
through the format provider and the `CreateNewUuids` merge that File then Import uses for a format
without identifiers, produced an entry holding both strings unchanged, in a user group
[M here, `out\import-plant.txt`; R KeePass/DataExchange/Formats/BitwardenJson112.cs:213-229,
KeePass/DataExchange/ImportUtil.cs:567-586, :191]. Every importer that carries custom fields by name
does the same [A for the others]. So the file an operator is asked to import "to add these accounts"
can carry a retirement of their authority.

**The outcome.** On the next request the fold finds a retirement of the only authority and the gate
serves nothing. The planted entry carries a `KPG.` string, so guard point 2 makes it absent to every
route: nothing the API shows reveals where it came from. The first write of the plugin's own
(Create, Recreate, Keep, a pairing, a revocation) runs the prune, which deletes the authority entry
for good and leaves a deletion record that refuses it from then on. Deleting the planted entry
afterwards restores nothing: the private key is gone, and every client must pair again. The same
shape plants a `revoked` record for a certificate a co-resident client can read, or `removed
<name>/<g>` for generations 1..N of each of a caller's profiles; and during a `503 undecided`, a
planted retirement of one candidate silently settles the choice the design says the operator must
make (E12).

**This is partly reachable on master too.** Master reserves no field name; `createField` refuses
control and format characters, climbs and over-long names, but nothing by prefix
[R src/KeePassGate/Spec/openapi.yaml:1356-1381; no `KPG.` check under `Vault` or `Pipeline`]. A
client holding `create` can write the two strings into an entry it may reach. The design's own guard
point 3 fixes the API path (`createField` refuses a `KPG.` name, other field routes meet an absent
entry), which is a genuine improvement and must ship with this option. The import and merge paths
are the ones that remain.

**Rating.** Serious. It fails closed (E1 holds), but it converts an ordinary operator import, or an
API `create`, into the permanent destruction of the authority, with the actor inside the threat
model. The design's section 11 bullet on a hostile file speaks only of a merged `.kdbx` with
matching identifiers; this needs neither.

**Change.** One of:

- **Narrow.** The prune acts only on a refusal at its own derived identifier as a direct child of
  the store group. A refusing record found anywhere else still refuses (fail closed) but is never
  deleted, is listed on the KPG tab with its group and title, and clears on the operator's word. A
  retirement found outside the store never settles a `503 undecided`. This keeps the merge-survival
  intent while making "believed everywhere" stop short of "deleted everywhere".
- **Strong.** Authenticate each record. The authority document already holds a secret per authority
  (`sniKey`); an HMAC over kind, subject and content, verified with the key of the authority the
  record is about, makes a record count only if the authority whose key signs it is present. A
  retirement of X verifies with X's own key, which is in X's entry whenever that entry is there to
  be refused; a revocation with the key of the certificate's issuer; profile records are re-signed
  under the new authority at a Recreate. This also closes the hostile-file retirement the design
  concedes in section 11, and the widening in M6. It reverses `CA.md`'s statement that nothing in a
  record is authenticated beyond the file (inventory P23), a reversal to record.

### S2. A record in the recycle bin returns on its own (serious)

Section 8 answers deleting a client's record with "the client is not admitted. Out of the bin it is
admitted again unless revoked", and section 1 point 5 claims "every way a person or a program can
damage the store therefore lands on the refusing side". A granting record in the bin is the
counterexample, and it needs no second act to come back.

**The mechanism.** The only removal gesture outside the KPG tab is Delete. An operator who wants a
client gone, in KeePassXC, on a phone, or in KeePass without opening the tab, deletes the `KPG
client ...` entry or the whole `KeePassGate` group to the recycle bin. KeePass recycles with
`Touch(false)`: location and its time move, the modification time does not
[R KeePass/Forms/MainForm_Functions.cs:5160-5166 for an entry, :5262-5279 for a group]. KeePassXC
2.4.2 and 2.6.6 `rm` leave the modification time as well [M keepassxc 6]; Keepass2Android and
KeePassDroid change no time [R android 1.4, 4.2]. The record is in the bin, so it is refused.

**The return.** Store entries never change, so across copies they share one modification time. A
stale copy made before the delete, still holding the record in the store group, merged with this
copy in KeePassDX 4.5.8: `mergeEntry` finds the times equal and moves the open copy's entry to the
arriving copy's group [R merge/DatabaseKDBXMerger.kt:519-524], which is the store group, recreated
first if the open copy binned it (`getAttachedParent`, :439-458). The move stamps no location time
[R element/database/DatabaseVersioned.kt:283-291]. `mergeGroup` moves a binned store group back the
same way at :586-590 (its "open group is newer" branch at :584 compares the group with itself and
never fires). The record is a direct child of the store group again, admitted, with nothing in the
file recording that it was ever binned. A later KeePass synchronize keeps whichever location its
target holds, the two location times being equal [A]. KeePassDX, which stamps the time when it
recycles, and KeePassXC from 2.7.12, which sets it on `rm`, are the escapes [R android 4.2;
M keepassxc 6].

**Rating.** Serious. By the design's own definition this is not a revocation, but it is the removal
the operator has in every program but the one tab, the design presents it as holding, and its undo
needs no person.

**Change.** A granting record the read finds inside the recycle bin, or outside its derived place,
is revoked by the plugin's next write of its own (a `revoked` entry for a client, a `removed` entry
for a profile addition) rather than merely ignored, and is shown on the tab until then. The store's
notes line and `docs/clients.md` must say that only the KPG tab removes a client; Delete only hides
one. Run the Android report's R6 against this shape to confirm the equal-time move. What no change
reaches: a bin move and its merge-undo that both happen before KeePass-with-the-plugin opens the
file.

### S3. The authority's private key is one copy, print or export away in every program (serious)

**Today** the authority document is a value of `PwDatabase.CustomData`. KeePass's Plugin Data tab
lists its key and its size, never its value [R KeePass/UI/UIUtil.cs:3864-3873,
Forms/DatabaseOperationsForm.cs:73-76]; only the XML export writes it [R inventory P26]; no ordinary
screen in any program shows it [A for the Android programs].

**In this design** the same 2,903-character key material is a protected custom string of an entry in
a group every program lists, and protected is not hidden from the operator's own tools:

- KeePass's entry context menu offers "Copy" for each custom string, one click to the clipboard
  [R Forms/MainForm_Functions.cs:858-881]; the entry dialog reveals it on request.
- KeePass's print, and the Customizable HTML export built on it, write every custom string with
  `ReadString()`, protected ones in clear, when "custom strings" is ticked
  [R Forms/PrintForm.cs:735-739, :900-920]; the print path never consults the searching switch
  [R PrintForm.cs]. The box defaults off [R App/Configuration/AceDefaults.cs:405-406].
- KeePass's KDB 1.x export folds every custom string into the Notes in clear
  [R DataExchange/Formats/KeePassKdb1.Write.cs:203-204, :280-295].
- KeePassXC's HTML export prints every custom string, protected ones in clear, by default
  [M keepassxc 8].
- A phone shows the field with a reveal and a copy control [A]; a copy on Android passes through the
  keyboard's clipboard history [A]. An operator's Delete leaves the key in the recycle bin; a
  Duplicate leaves a copy the prune never touches (the design ignores "anything else in the group").

The design's section 11 concedes "more ways to leak it by accident" but reasons that "whoever holds
the master key holds everything either way". That understates the exposure, because **the authority
key is worth more than the entries it sits beside.** Holding the master key is reading the database
once; holding the authority's private key is the power to mint client certificates and admission
records for that database and, while the operator still trusts the gate, to be admitted by it with
any profile, offline, until the authority is recreated and every client re-paired [R CA.md:54-64,
:486]. The present design keeps that key in the one place the format hides from casual view; this
option moves it into the most-exported, most-copied, most-screen-visible part of the file. A protected
string is kept encrypted in KeePass's process memory, which the design cites as a gain over the
present plain custom-data string [design section 3], and that at-rest point is fair; it does not
offset the new display, print, export and clipboard surfaces, which are what leak a secret by
accident.

**Rating.** Serious. It widens the ways the highest-value secret in the file leaks, against E10's
spirit (secrets protected by the database's encryption and not put somewhere more exposed).

**Change.** Keep the authority document where it is, in `PwDatabase.CustomData`, and move only the
merge-fragile data (records, revocations, retirements, the list, the answers) into entries. The
authority is written once and never changed and already survives every KeePass merge and every save
as one add-only key [M spike; inventory P2], so it gains least from the move and loses most. This
is a hybrid, and the design's section 3 ("why one string for the whole authority") does not argue
against it; it argues only about splitting the authority across three strings. If the authority must
move too, mark its entry so KeePass excludes it from print and export, which no flag on an entry
actually does [R PrintForm.cs reads the group searching switch nowhere], so this sub-change has no
mechanism and the authority should stay in custom data.

### S4. The refusing records are durable visible clutter, and removing them undoes a revocation (serious)

Retirements and revocations are "only ever added" and never pruned until the certificate expires
(revocations) or forever (retirements) [design 3.1, 3.2]. As entries, they accumulate in a group the
operator sees in every program, each a `KPG revoked ...` or `KPG retired ...` line with empty user
name and password. A database that has rotated its authority twice and revoked a dozen clients shows
fifteen-plus cryptic entries that the operator cannot safely delete:

- Deleting a `revoked` or `retired` entry to the bin leaves it counting (design section 8), but a
  later Empty Recycle Bin, or Database Maintenance's "remove deleted objects information", takes the
  last trace from that file [R keepass 5]. The record it protected was deleted at revocation time, so
  that file now admits nobody; but a stale copy merged in afterward brings the granting record back
  with nothing left to refuse it. The design lists this (section 8, "Then runs Database
  Maintenance"), rating it "three deliberate acts and a stale copy". The understatement is that the
  first two acts are the ordinary way a tidy operator clears a cluttered group they were never told
  to leave alone: Delete, then Empty Recycle Bin. The store's notes line ("Managed on the KPG tab")
  is the only thing standing between the operator and this, and it is one line in a group whose
  entries look inert.
- pykeepass deletes an entry with no deletion record at all [M here, `out\pk-delete.txt`: 0
  `DeletedObject` elements after deleting a store entry and saving], so a GNOME Secrets or passhole
  user who tidies the group removes the tombstone and the refusal in one step, no Empty Recycle Bin
  needed.

**Rating.** Serious, because the clutter is permanent and the safe-looking action on it (Delete the
old junk) is the one that reopens E3/E4 from a stale copy. The present store keeps all of this in
custom data, which only Database Maintenance's Plugin Data tab exposes, and deleting a key there is a
deliberate act in a place an operator rarely opens [R inventory P11].

**Change.** Two parts. (a) The strong variant of S1 (authenticated records) makes a resurrected
granting record fail to verify, so clearing an old refusing entry no longer reopens admission; this
is the single change that most reduces S4's blast radius. (b) Give the store group `EnableSearching`
off and a notes line, which the design already does, and additionally render the refusing entries
with a title that names them as safe-to-ignore machine records and documents, in `docs/clients.md`
and the store's notes, that the KPG tab is the only safe way to remove them. Neither part removes
the clutter; the design accepts durable retirements as the price of merge survival, and that trade
is sound.

### M1. Every request walks the whole tree on KeePass's thread (minor)

The read pass is "one walk over every entry of the tree and every deletion record" (section 4), on
KeePass's thread, on every request including `getStatus` and `getIdentity`, which read nothing a rule
is about [R src/KeePassGate/Spec/openapi.yaml:256, :308]. Today those two, and every other request,
read the database's own custom data dictionary, not the tree [R inventory A6; OpenDatabaseTargets].
The design measures 1.9 ms for 10,054 entries in a console [M store-entries], and offers an icon
test to cut it. Two caveats the design's own number does not cover: the measurement is a console
process with nothing else on the thread, while the gate shares KeePass's UI thread with whatever
KeePass is doing; and a flat `GetEntries(true)` plus a scan of `DeletedObjects` is work the present
store never does for a status ping. A1 (cheap reads) is a stated requirement.

**Rating.** Minor. Likely acceptable on real databases, but it should be measured on KeePass's own
thread for 1k/10k/50k entries (the design's own measurement item 4) before this is chosen, and the
cheap routes (`none` verb) should perhaps be answered without the tree walk, from the authority in
custom data, if the authority stays there (S3's change).

### M2. "Searching off" does not keep the store out of KeePass's own views (minor)

The design relies on `EnableSearching` false to keep the store entries out of sight (section 2, 8).
It keeps them out of quick find, the password reports and the expiry warning
[R keepass 9]. It does not keep them out of: Find, Show all (`PerformSearchQuick(string.Empty, true,
false)` passes `bRespectEntrySearchingDisabled` false) [R Forms/MainForm.cs:2391,
Forms/MainForm_Functions.cs:2250]; the Large entries and Last modified reports, which list every
entry [R keepass 9, EntryUtil.cs]; and `{REF:}` resolution, which ignores the switch
[R SprEngine.cs:650]. So an operator doing Edit, Show All Entries, or running those reports, sees the
store entries among their own. This is cosmetic for the refusing records but, combined with S3, is
another route by which the authority entry is in front of the operator.

**Rating.** Minor. The design already lists "the large entries and last modified reports list them"
(section 8); the Find-all and reference points are additions. No fix beyond documenting it, unless
S3's change keeps the authority out of entries entirely.

### M3. Two healthy profile lists can merge to an unreadable list (minor)

The list is "the set of names with a live addition" (3.3), and "more than 32 names ... reads as a
list that will not read, which is an empty list ... every client stays admitted and reaches
nothing". Additions are one entry per name per generation and merge as a union [design 3.3]. So two
copies that each add a different handful of names, both under 32, can merge to a union over 32, which
flips every client to reaching nothing until the operator prunes names. The present store has the
same 32 ceiling [R inventory D6] but one merged value, so it cannot exceed what one copy wrote; the
union changes that.

**Rating.** Minor, and fail-closed (reaches nothing, not everything). But it is a new way for
ordinary concurrent edits to deny service, and the design's section 3.3 does not mention it. Note it,
and consider that the ceiling should count distinct names across live additions with a headroom, or
that crossing it should be surfaced on the tab rather than read as "unreadable".

### M4. A carried-over database keeps the old format and the old key exposure (minor)

Section 13 leaves migration undesigned, offering either reading the old `KPG.*` keys indefinitely
(which the project's no-legacy rule forbids) or treating a 0.1.0/master database as having no
authority. If an operator's live database is migrated by writing entries but the old custom-data
keys are not removed (because removing them while a stale copy could restore them is itself unsafe),
the file keeps its per-key times and stays KDBX 4.1, in the KeeWeb-unreadable class [M others 5], and
keeps the authority key in plain custom data, so S3's new exposure is added to rather than replacing
the old. The design's claim that "the database is no longer taken to 4.1" (section 10) holds only for
a database born under this option.

**Rating.** Minor, and the design is honest that migration is undesigned. Flagged so the format and
key-exposure claims are read as "for a fresh database".

### M5. The absence guard hides pre-existing entries, and `409` still leaks existence (minor)

Guard point 2 makes "any entry anywhere that carries a string whose name begins `KPG.`" absent to
every route. On a database upgraded from master, a client may already have created entries with
`KPG.`-named fields through today's unreserved `createField`; after the upgrade those entries, and
whatever else the client stored in them, vanish from the API with no explanation. Separately,
`createField`'s `409` on a reserved name (guard point 3) tells a caller that the name is taken or
untellable-from-taken, which is a sliver of existence information the design's "refused and absent
answer the same 404" principle elsewhere avoids; the design routes it to `400 malformed`, which is
better, but any entry that already holds a `KPG.` string is absent by point 2, so a client that put
one there pre-upgrade loses the whole entry.

**Rating.** Minor. Consequence of M4; disappears if there is no carry-over. Worth a line in the
migration note.

### M6. "Written once, never changed" is discipline, not an invariant (minor)

Several claims (3, 4, 7's "every store entry in every result has the strings it was written with")
rest on the plugin never changing a store entry after writing it. KeePass cannot enforce that: an
operator, or any program, can edit a store entry's strings, and then the whole-entry merge rules the
ground reports document apply, with ties and history losses. The design handles operator edits in
section 8 ("a record edited into another valid record takes effect ... it takes the master key"),
which is the right fail-closed answer, but the serenity of "the store never puts two different
versions of one entry into the world" (end of section 7) is a statement about the plugin's own
writes, not about the file, and an edited store entry breaks it. This is the same class as S1/S6 and
is the reason the strong (authenticated) variant is worth its cost.

**Rating.** Minor on its own; it is the general case S1 and S2 are instances of.

### M7. Smaller write-rule gaps (minor)

- **Duplicate.** Section 8 says a duplicated entry "grants nothing, and refuses if it is a refusing
  record". A duplicated `revoked` or `retired` entry thus refuses under a new UUID; the prune (keyed
  on derived identifiers as direct children) never removes it, so a Duplicate of a refusing entry is
  permanent clutter that also, under the strong variant, must still verify. Minor, but the prune's
  reach should be stated for duplicates.
- **`createField` reserved-name check is case-insensitive over ASCII** (guard point 3), while
  KeePass compares string names with its own culture rules; a name differing only by a Turkish dotless
  i could slip past an ASCII compare and yet be a name KeePass treats as `KPG.`-like. The design says
  "compared without regard to ASCII case"; confirm that matches how the store's own reader tests a
  name, or a caller could create a near-miss that the reader later treats as the plugin's. Minor.

### M8. The test line understates the rewrite (minor)

Section 6's test row says `CustomDataMergeRuleTests` "gives way to" a few new pinning tests. The
real surface is larger: 49 test files touch the database-level store or its merge behavior by name,
about 26,000 lines and 734 test methods [M here, `affected-tests.txt`]. Not all are rewritten, but
the seam change (`IKeyValueStore`/`CustomDataStore` go; `AdmissionStore`, `AuthorityStore`,
`ProfileNames`, `RenewalRefusal`, `PendingChanges`, `DatabaseWriter`, `OpenDatabaseTargets` all
change) touches a large fraction, and new host tests are needed for every row of section 7 against
the pinned KeePass, plus the recycle-bin, import-plant, and equal-time-move hazards this critique
raises. The design's "Tests" row reads as a tidy swap; the cost is closer to a tier rebuild.

**Rating.** Minor (it is a cost estimate, not a defect), but worth correcting so the decision is made
with the real number.

## What it costs in this code base

- **Classes rewritten or removed**: `IKeyValueStore`, `CustomDataStore` removed; a record-seam class
  added to `Bridge`; `AuthorityKeys` becomes kinds/subjects/derivation; `AuthorityStore`,
  `AdmissionStore`, `RenewalRefusal`, `ProfileNames`, `PendingChanges`/`PendingStore`/`PendingDatabase`,
  `DatabaseWriter`, `OpenDatabaseTargets`, `GroupDialogs` (drop the maintenance-records watch),
  `KdbxVault`/`VaultHandlers`/`DatabaseNodes` (the new API guard), `Expected.OfCustomData`, and the
  Ui classes that read the store all change [R the grep of 42 source files touching these names].
  `AuthorityDocument` and `AdmissionRecord` formats are unchanged, as the design says.
- **Measured facts that fall away**: "A KeePass merge of `PwDatabase.CustomData` visits the source's
  keys ... never reads a value" stays true but stops being load-bearing; "The authority takes the
  file to KDBX 4.1" is deleted; the `DatabaseOperationsForm` paragraph of the trap section stops
  being about the store. Each is a recorded reversal the project's conventions require.
- **New hazards needing new tests**: the import-plant (S1), the recycle-bin return (S2), the
  equal-time KeePassDX move (S2), the >32 union (M3), the reserved-name case rule (M7). These are
  host and end-to-end tests, several needing the emulator the design's section 14 defers.
- **0.1.0 carry-over**: not possible within the no-legacy rule except by reading the old keys
  indefinitely (forbidden) or treating an old database as authority-less (re-pair). Either way the
  operator acts once by hand; M4/M5 are the residue if keys are left in place.

## Problems the design states that I confirm, and do not re-raise

A file written over by a whole-file synchronizer or an overwrite save (section 11, bullet 1), a stale
copy on its own, deletions not travelling through KeePassXC/KeePassDX merges, and a hostile merged
`.kdbx` under a matching-identifier method: all real, all the design's own, none made worse by my
findings except that S1 shows the hostile-input surface is wider (import and `create`, not only a
merged `.kdbx`), which the strong variant of S1 closes.
