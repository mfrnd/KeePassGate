# Critique of option `versioned-fields`: security, KeePass's own behavior, the operator, the cost

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. Key:
`versioned-fields-security-and-fit`. It attacks `option-versioned-fields.md` ("every write a JSON
document in an entry field named from its time, reconciled when read") through four lenses: the
caller in the threat model and an attacker-supplied file; where the authority's private key shows up;
what KeePass's own functions and the present guards do to this store; what the operator sees and can
do wrong; and what the change costs in this code base. It credits what the design gets right, because
a fair attack says where the armour holds, and it does not repeat the design's own section 12 except
where that section understates a problem. Each weakness is paired with the change that closes it.

## How statements are marked

- **[M here]**: measured for this critique, from a console, with invented content. Script and output
  under
  `spikes\storage\probes\versioned-fields\security-and-fit\`:
  `scripts\copies.ps1`, `out\copies.txt`, over KeePass 2.62's KeePassLib loaded from
  `.keepass\2.62\KeePass.exe`. No window.
- **[M vf]**: measured by the design's own probe, as `option-versioned-fields.md` section 14 records
  it (its files `out\*.txt`); I read the design's claims, not every output file.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in that ground report,
  by `spikes/revocation-merge`, or by the design's runs.
- **[R file:line]**: read from source. KeePass 2.62 is `KeePass-2.62-Source.zip` as the `keepass`
  ground report extracted it, paths from the archive root. KeePassGate is `a42e38aa` in
  the product at `a42e38aa`.
- **[A]**: assumed. Neither run nor read.

## The verdict

**Viable with changes.** No finding is fatal: the design's essential aims — fail closed, a narrowing
never undone, a revocation that survives every merge — are reachable by what this design is, and the
measured results support them for KeePass and KeePassXC. The store identifier, the mark-redraw and
the `KPG.` refusal together **defeat the import-plant and client-plant attacks that were serious for
`store-entries`** (the section "What was checked and holds"), which is the design's largest security
gain, and its rule survival is the strongest of any option I have read. But three problems are
serious, and each has a named change.

| # | Rating | Problem |
| --- | --- | --- |
| S1 | serious | The core merge-survival claim is unmeasured on the Android programs that drove the rethink, and the dialog write-back is unmeasured too; both are deferred to an emulator and a Live run the design has not done |
| S2 | serious | The authority's private key moves into a string field, exposed to print, HTML export, the clipboard and a phone's screen in every program, for a merge gain the authority does not need |
| S3 | serious | One ordinary operator action on now-visible, junk-looking data takes the whole gate dark (`503 damaged`), recoverable only on a new page, and the data is in front of the operator in every program |
| M1 | minor | Delete Duplicate Entries deletes the original group marker after a plain Duplicate Group with title-extend off |
| M2 | minor | A deletion record carrying the known 4-byte write mark takes the whole gate down, if an operator can be made to Synchronize a hostile file |
| M3 | minor | Every closing write rewrites and churns a visible group; the write amplification and the deletion records it sheds are a cost the design measures only in passing |
| M4 | minor | A listing does two custom-string lookups an entry on KeePass's thread, 12 ms at 50,000 entries |
| M5 | minor | The rule's third fallback is history, which K2A prunes at every load and Database Maintenance by age; a narrowing that reaches only history can be lost |
| M6 | minor | A carried-over 0.1.0 database keeps the old keys, the old key exposure and KDBX 4.1 until the operator finishes by hand |
| M7 | minor | The test and class line understates a tier rebuild |

## What was checked and holds

So the design gets credit for what it got right, and these are not re-raised as problems.

1. **An API client cannot plant a version that counts, and cannot forge one.** Every version must
   carry the home store identifier, 32 random hex drawn once and never exposed in any answer, tag or
   audit line [design 3.2, 9.6], and a version of any other store "counts for nothing" [design 5.2,
   measured F1: a marked entry planted under another store identifier with a retirement and a wide
   rule changed nothing [M vf]]. A client cannot read the identifier: `KPG.`-named strings are
   absent to every field route and marked entries are absent whole [design 9.1-9.3]. This is the
   decisive difference from `store-entries`, where the refusing record's subject (the authority
   digest, in every client's `authority.pem`) was derivable by a legitimate client and believed by
   its content wherever found [`critique-store-entries-security-and-fit.md` S1]. Here the content is
   not enough; the secret identifier is needed, and the client never has it.
2. **An API client cannot reach `503 damaged`.** I worked every route: `createEntry` carries only the
   five standard fields, no custom one [R src/KeePassGate/Spec/openapi.yaml:1982-2036]; `createField`
   refuses a `KPG.` name and the other field routes meet an absent field [design 9.3]; a created
   entry or group gets a random identifier, redrawn if it carries one of the three marks [design 9.4;
   R Bridge/KdbxVault.cs:385, :603]; a marked entry and the plugin's group are absent and cannot be
   removed [design 9.1, 9.5]. So none of the `503 damaged` triggers (a planted deletion record, two
   contents under one identifier, an unreadable version field, a foreign-store home version) is
   reachable by the caller. The caller cannot make the gate misread ordinary data as its own, because
   the data it would need to shape is either a `KPG.` string it may not write or a UUID it may not
   choose.
3. **The rule survives better than in any other option.** A rule has three witnesses: the state pair
   (two entries, each holding the newest rule version of every node), and the node's own copy
   (`KPG.Rule` on an entry, the marker in a group) [design 4.2]. The node copy is never the store, so
   a user entry edited later in another copy, which takes that entry whole and pushes the rule to
   history, loses nothing: the state pair still carries it [design 4.3, measured pair H5: a narrowing
   survived in all 16 merges]. For a narrowing to widen, all three witnesses must fail to carry it
   and an older wider version must surface, which is the deep case M5 names. This meets E5 and E6, the
   requirements the whole rethink exists for, where every carrier-on-the-node option fails.
4. **Opening or reading writes nothing (O1 kept).** The reconciling rule is a read-time computation;
   tidying rides only on the plugin's own writes [design 5, 9]. The operator ruling that opening
   changes nothing in the file stands.
5. **The file is KDBX 4.0.** The store uses strings, protected strings, the group's two flags and
   deletion records, all 4.0, and no node custom data and no per-key database-custom-data time
   [design 10; R KeePassLib/Serialization/KdbxFile.cs:351-396 as `store-entries` read it]. The
   database is no longer forced to 4.1, and a fresh one leaves behind the KeeWeb-unreadable class
   (timestamped custom data in a 4.1 file) [M others 5].
6. **A copied node refuses rather than inherits.** A duplicated ruled entry keeps `KPG.Rule` naming
   the original, and a duplicated ruled group keeps a marker naming the original group, so the copy
   is recognized as a copy and refuses until settled [design 5.3; confirmed M here: after a Duplicate
   Group the copy's marker `KPG.GroupRule` still named the original group's UUID, and the entry
   duplicate's `KPG.Rule` still named the original entry]. This is the fail-closed direction E5 wants.

## Problems

### S1. The design's core claim is unmeasured on the programs that caused the rethink (serious)

The project's decision of 2026-11-15 was driven by the Android programs and KeePassXC breaking the custom-data
store [the task; M spike §5, §6]. The design answers with one-entry-per-write, whose whole promise is
that two write entries added in two copies both survive a merge. That promise is **measured for
KeePass 2.62 and `keepassxc-cli`** [M vf, 160 merges] and **only read for Android**: section 7.2's
Keepass2Android row says in as many words "Not measured for this shape," and section 15 lists items 1
and 2 — the Android merges and every Android save of the store — as the two most important
measurements still to make, needing the emulator and the free desktop. So the design is unvalidated
on Keepass2Android and KeePassDX for exactly the inputs that motivated it.

The reads it rests on are not reassuring at the edges. KeePassDX 4.5.8 moves an open entry to the
arriving copy's group on equal modification times [R android 1.1, DatabaseKDBXMerger.kt:519-524], and
write entries are never changed, so two copies of one write entry share one modification time
[design 5.4] — the equal-time case is the normal case here, and it is the case the reads cover least.
KeePassDX 3.2.0 **silently drops** an arriving entry whose group is missing in the open copy
[R android 1.5], which for a write entry whose `KeePassGate` group the open copy binned or lost is a
lost write. Keepass2Android trims history at every load [R android 1.3]; the design's fallback to
history for a node's rule (M5) meets that head-on. None of these is run for the store's shape.

A second mechanism is unmeasured too. Section 6.4 writes the rule into `PwEntryForm.EntryStrings` and
adds the state pair from a `FormClosed` handler, and marks the key behavior assumed: "KeePass's own
question on Escape ... is asked because the entry's strings changed [A until item 3 of section 15 is
run]." So the OK, Cancel and Escape behavior of the grant editor — the thing the `GroupForm` trap
makes load bearing — is an assumption, not a measurement, on this design.

**Rating.** Serious. The design cannot be chosen on a claim that is read rather than measured for the
programs the rethink is about, and for a dialog mechanism the project treats as load bearing
everywhere else.

**Change.** Run section 15 items 1, 2 and 3 before choosing: the Keepass2Android and KeePassDX merges
and saves over the store's shape (the Android report's R1-R10 already specify the bases and flows),
and the entry and group dialogs in a real KeePass 2.62. Until item 1 reads back a surviving write
entry under every Android merge, E3 and A1 are hopes on this store, not results.

### S2. The authority's private key moves into an exported, copyable, on-screen field (serious)

**Today** the authority document is a value of `PwDatabase.CustomData`. KeePass's Plugin Data tab
lists its key and size, never its value; only the XML export writes it; no ordinary screen in any
program shows it [R inventory P26; `critique-store-entries-security-and-fit.md` S3].

**In this design** the same 3,063-character material — the authority certificate and key, the host
PKCS#12, and the gate key — is a protected custom string of an entry in a group every program lists
[design 3.2 row 1, 4.2 row 1]. Protected is not hidden from the operator's own tools: KeePass's entry
menu copies a custom string to the clipboard in one click, KeePass's print and its HTML export write
every custom string with protected ones in clear when "custom strings" is ticked, KeePassXC's HTML
export prints them in clear by default, and a phone shows the field with a reveal and a copy control
[all read in `critique-store-entries-security-and-fit.md` S3, PrintForm.cs:735-739, MainForm_Functions.cs:858-881,
M keepassxc 8]. The design concedes this in section 12 ("The store is in plain sight, and the
authority's key with it").

It understates it in two ways. First, it is framed as the price of the design — "Keeping the
authority alone in the database's custom data would hide it again, at the price of the one place this
design otherwise leaves behind" — but the price is zero. The authority is written once and is
**identical in every copy**, so a merge never has to choose between two authorities; the only
authority-related datum a stale copy can get wrong is the **retirement**, which is a separate, tiny,
closing version. A write-once custom-data key already survives every KeePass merge and every save
[M spike; inventory P2], and the only merge that drops it is a KeePassXC merge that replaces the whole
dictionary [M spike] — which loses an authority that is the same in both copies, and so loses nothing.
The design's own section 4.2 justification for making the authority an entry ("so the key leaves the
file when that entry is deleted at the retirement") is served equally by the present prune, which
removes `KPG.Authority.<fp>` once its retirement exists [R inventory D1]. So moving the authority to a
version buys no merge survival the custom-data key does not already have, and pays the full new
exposure.

Second, "A leaked key alone admits nobody" is true for the admission path and understates the rest:
the version carries the host PKCS#12 and the gate key as well, with which a holder can stand up the
gate's own TLS endpoint and SNI and impersonate it to a client on the same machine [design 12,
conceded], and the exposure is that the highest-value secret in the file now travels out through
print, an HTML file and the clipboard — channels a custom-data value never crosses — so it escapes
the database's encryption to somewhere the operator did not choose.

**Rating.** Serious, against the spirit of E10 (secrets protected by the database's encryption and
not put somewhere more exposed).

**Change.** Keep the authority document in `PwDatabase.CustomData`, and move only the merge-fragile
data — records, revocations, retirements, the list, the answers, the rules — into versions. This is
the hybrid the `store-entries` critique reached for the same reason, and here it costs even less,
because the authority is the one datum that already survives everything the version machinery is for.

### S3. One ordinary act on visible junk takes the whole gate dark (serious)

The design concedes "A hand deletion closes the whole gate" (section 12) as an edge case. The
understatement is that the data is now **in front of the operator in every program**, looks like
junk, and invites the very acts that trigger `503 damaged` — so the edge case is the ordinary one.

What the operator now sees, in KeePass, KeePassXC and on a phone: a `KeePassGate` group holding the
authority entry, a state pair of two big entries titled `KPG <time> state` and `... (second copy)`,
and a growing pile of small entries `KPG <time> client paired` and the like — 520 small entries a
year on the design's own busy example, tidied to a state pair every eleven days [design 6.3] — each
with an empty user name and password and a line of notes. And on every ruled user entry, an extra
`KPG.Rule` string holding JSON; in every ruled group, a `KPG rule for this group` entry. A tidy
operator meeting fifteen cryptic machine entries and a group full of dated `state` duplicates has
every reason to clean them up, and each of these does it harm:

- Editing a version field, or deleting one write entry with a deletion record, or deleting both state
  entries, or deleting the group: `503 damaged` until the operator finds and uses the new Data page,
  or (deleting both state entries) every client gone from that file [design 8, measured H2-H6].
- A routine **Delete Duplicate Entries** after an import with new identifiers deleted four originals,
  the authority's entry and a marker among them, and the design's own run read that back as
  `503 damaged` recovered only by "Put back" on the Data page [design 7.2, 8; M vf]. This is a
  self-inflicted outage from a maintenance command the operator runs for unrelated reasons.
- A `revoked` or `retired` entry is permanent by design and never prunes [design 6.3], so the clutter
  only grows, and the one safe remedy (the KPG tab) is the one place a hurried operator does not go.

The present store keeps all of this in custom data, which only Database Maintenance's Plugin Data tab
exposes, and deleting a key there is a deliberate act in a place an operator rarely opens [R inventory
P11]. This design trades that invisibility for a group of inviting junk whose wrong handling denies
service to every client at once.

**Rating.** Serious. It fails closed, so no essential requirement breaks, but it converts ordinary
tidying into a gate-wide outage and raises the likelihood of the conceded hand-deletion far above an
edge case, because the data is now visible and looks disposable.

**Change.** Take S2's hybrid (the authority leaves the group, so the group holds no secret and the
export surface shrinks); title every machine entry so it reads as a managed record not to be touched,
keep the group's notes line and `EnableSearching` off, and say in `docs/` and on the Data page that
the KPG tab is the only safe way to remove a client or a rule and that Delete only hides. None of this
removes the clutter; the design accepts durable retirements and state churn as the price of merge
survival, and that trade is where the next finding bites.

### M1. Delete Duplicate Entries deletes the original group marker (minor)

The design protects the two state entries from Delete Duplicate Entries by differing titles and a
`KPG.Write` field [design 4.1], which I confirm holds for them. The group **marker** is not so
protected. KeePass's Duplicate Group copies the marker, and its ApplyTo appends " - Copy" to each
entry's title only when the operator leaves "append Copy to title" ticked; with that box cleared, the
copied marker is string-identical to the original. Measured [M here, `out\copies.txt`]: with
title-extend on, Delete Duplicate Entries deleted nothing; with it off, Delete Duplicate Entries
deleted the pair and **the original marker was the one removed** (it and the copy tie on modification
time, and KeePass deletes the earlier of a tie, `PwDatabase.cs:2002` [R]). The rule survives in the
state pair, so this costs nothing to authorization, but it is one more way an ordinary command rewrites
the store, and the design's "Delete Duplicate Entries ... does not take one for the other's
duplicate" (section 4.1) is true of the state pair and not of the marker.

**Rating.** Minor. Survivable. Note it on the marker, and consider giving the marker a `KPG.Write`-like
distinguishing string as the state entries have.

### M2. A planted deletion record with the known write mark denies service (minor)

The write mark is the first four bytes of SHA-256 over `KPG/1/write`, a public constant [design 3.1],
so the prefix of a deletion record that trips `503 damaged` is derivable by anyone. A deletion record
is "the plugin's own" only when its time is an entry's-time-plus-one-second [design 6.3]; any other
deletion record carrying the mark is read as a hand deletion and serves `503 damaged` [design 5.5].
A deletion record with the mark prefix and twelve arbitrary bytes, time not matching the plugin's
pattern, therefore denies service without naming any real entry. KeePass carries deletion records
only through Synchronize, not through the CreateNewUuids import a foreign file gets [M keepass §5], so
the carrier is an operator talked into "Synchronize with this file" over a hostile `.kdbx`, which is
unusual. It is recoverable on the Data page, unlike the `store-entries` import-plant, which destroyed.

**Rating.** Minor. Requires an operator to Synchronize a hostile file; recoverable. Worth stating
that `503 damaged` is cleared on the Data page and nothing is lost by it.

### M3. Every closing write churns a visible group, and sheds deletion records (minor)

A write that closes something writes the whole state twice (two state entries, ~17,000 characters
each in the design's example, more with large rules) and then deletes every other write entry except
the authority's and any open in a dialog [design 6.1, 6.3]. So a revocation, a retirement, a profile
removal and every rule change rewrite ~34 KB and delete a pile of entries, each deletion leaving a
record — some 585 a year on the busy example [design 6.3]. The save is a whole-database write
(measured at tens of seconds for a large database elsewhere [inventory, DESIGN.md §119]); the churn is
in a group the operator watches; and the deletion records accumulate until Database Maintenance clears
them, which the design relies on not needing but which the operator may run. The design measures the
**read** cost (5.6) and sizes the store (6.3) but does not measure the **write** and save cost of the
churn, which section 15 item 6 defers.

**Rating.** Minor. Measure a closing write and its save at the store's largest (section 15 item 6)
before choosing.

### M4. A listing does two node lookups an entry on KeePass's thread (minor)

A full listing looks up `KPG.Rule` and `KPG.GroupRule` on every user entry, which the design measures
at 1.5 ms for 10,000 entries and 12.0 ms for 50,000 for one such lookup [design 5.6], so two lookups
double it, on KeePass's own UI thread, on top of the two rows every request pays. Today a listing
reads node custom data, measured at 0.55 ms for 10,060 entries [M rules-central]. It is likely
acceptable but is new work on the shared thread (A6), and should be measured on KeePass's thread, not
a console, at 10k and 50k entries (section 15 item 5).

**Rating.** Minor.

### M5. The rule's third fallback is prunable history (minor)

When both state entries are gone and the node's own copy is not the newest, the design reads the
node's history for a newer rule version [design 5.3, measured H5]. History is not durable:
Keepass2Android trims it to ten items at every load [R android 1.3], KeePass's Database Maintenance
deletes items older than 365 days [R keepass 1b], and a node edited often (a password rotated weekly
through the API adds a revision each time) pushes older items out. So a narrowing that reaches only
history, after both state entries are lost, can itself be lost, and the node then inherits wider. This
is deep — it needs both state entries gone and the node reverted to an older copy and the newest rule
aged out of history — and the measured H5 case kept the narrowing, but the design's reliance on
history is a real fail-open seam where everything else is fail-closed.

**Rating.** Minor, because the triple-witness design makes the precondition narrow. Note that the
history fallback is the one place a narrowing is not guaranteed.

### M6. A carried-over database keeps the old exposure until finished by hand (minor)

Section 13 leaves migration to a press on the KPG tab that writes the new store and only then removes
the old keys. Until that press, or if a stale unconverted copy is merged in later, the file keeps the
old `KPG.*` custom-data keys, their per-key times (so KDBX 4.1, the KeeWeb-unreadable class [M others
5]) and the authority key in plain custom data — so S2's new exposure is added to the old rather than
replacing it, and the section 10 claim of KDBX 4.0 holds only for a database born under this option.
The design is honest that migration is undesigned; this flags that the format and exposure gains are
"for a fresh database."

**Rating.** Minor.

### M7. The class and test line understates a tier rebuild (minor)

Section 6.5 lists about twenty classes changed and calls the test change "a rebuild of that part of
the tiers," citing the `store-entries` figure of 49 test files touching the present store by name.
That is the honest headline, but the design adds surface the `store-entries` critique did not: a
fourth tab page (Data) with its own operations, a new `503 damaged` path through the whole response
layer, a reconciler of real intricacy (home/not-home, opens-from-current, closes-from-anywhere,
cumulative `after`, strictest answer, what-both-allow), and new host tests for every row of section 7
plus the recycle-bin, import, Duplicate, equal-time-move and Delete-Duplicate hazards, several needing
the emulator section 15 defers. The reconciler is itself a security surface: it decides authorization
on every request from several sources, where today a rule is one document from one place and a record
one key.

**Rating.** Minor (a cost estimate, not a defect), but the decision should be made against the real
size, which is a reconciler and a page, not a seam swap.

## What it costs in this code base

- **Classes**: `IKeyValueStore` and `CustomDataStore` go; a version-store seam, a `Version` envelope,
  a reworked `AuthorityKeys`, and the Data page are added; `AuthorityStore`, `AdmissionStore`,
  `RenewalRefusal`, `ProfileNames`, `PendingDatabase`/`PendingStore`/`PendingChanges`, `DatabaseWriter`,
  `OpenDatabaseTargets`, `KdbxNodes`/`DatabaseNodes`, `NodeDialog`/`IGrantStore`, `GrantPanel`/`GrantSheet`/`GrantTab`,
  `GroupDialogs`, the settings tab, the vault guard and `Expected.OfCustomData` all change [design 6.5].
  The S2 hybrid shrinks this a little: `AuthorityStore` keeps writing the authority to custom data.
- **Measured facts that fall away or reverse**: "The authority takes the file to KDBX 4.1" is deleted;
  the whole `CustomData`, per-key-time and merge-rule bullet is rewritten; `CA.md` §204 and §208; the
  `DatabaseOperationsForm` paragraph of the trap section stops being about the store; the grant editor
  no longer writes `m_sdCustomData`. Each is a recorded reversal the conventions require, as the design
  lists in section 11.
- **New hazards needing new tests**: the dialog write-back into `EntryStrings` (S1, unmeasured), the
  Android merges and saves (S1, unmeasured), the Duplicate-marker deletion (M1), the deletion-record
  `503 damaged` (M2), the state churn and save cost (M3), the listing read cost on the thread (M4), the
  history-fallback narrowing (M5). Several need the emulator the design defers.
- **0.1.0 carry-over**: a one-time press on the KPG tab, with the residue of M6 if the old keys are left
  for merge safety.

## Problems the design states that I confirm, and do not re-raise

A file written over by a whole-file synchronizer or an overwrite save; a stale copy on its own and the
21 cells where both witnesses are destroyed and the copy is merged into a stale one by a
tombstone-less method; deletions that leave no record (pykeepass, KeePassDX); every copy of a ruled
node refusing until settled; a rule changed in two copies being narrower than either meant until
settled; a file made by somebody who once held the database knowing the store identifier; the clutter
of a marker per ruled group and a field per ruled entry. All real, all the design's own (section 12),
and none made worse by my findings except that S2 and S3 show the authority exposure and the
operator-facing fragility are larger and likelier than section 12 frames them.
