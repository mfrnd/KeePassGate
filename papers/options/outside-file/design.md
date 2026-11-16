# Option `outside-file`: the authority stays in the file, everything that changes moves to a sealed folder beside it

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. One design, the strongest
form of "outside the `.kdbx`" that I could find. It covers all eight data. It is not a survey: section
3 has the variants the task names and why each lost.

## How statements are marked

- **[M here]**: measured for this report, today, from a console: KeePass 2.62's KeePassLib,
  `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, and a prototype of the store, all over invented
  content. Section 20 describes the runs and names the files.
- **[M spike]**, **[M inventory]**, **[M keepass]**, **[M keepassxc]**, **[M others]**: measured in
  `spikes/revocation-merge` as `ground-measured.md` and `ground-inventory.md` digest it, or in the
  ground report of that key.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[R 2.62]**: read by me in `KeePass-2.62-Source.zip` (the copy the `keepass` ground report
  verified), file and line given. **[R plugin]**: read in the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read. Every assumed statement about another program comes back
  in section 21 as a measurement to make.

## 1. The design on one page

1. **Two things stay in the file, both written once and never changed**, both in the database's
   own custom data where the authority already is: the authority document, `KPG.Authority.<fp>`,
   exactly as today, and a new key, `KPG.Store.<id>`, holding 32 random bytes. Nothing is written on
   any group or entry.
2. **Everything that changes or accumulates leaves the file**: retirements, admission records,
   revocations, standing answers, the profile names and every access rule. They live in a folder
   beside the database, `<database file name>.kpg`, which no program that opens a `.kdbx` ever
   reads or writes.
3. **The folder is bound to its database by a key and not by a name.** Every file in it is sealed
   under the 32 bytes the database holds. A database that does not hold them cannot open the store,
   and a store not sealed under them is not the database's. Nothing on the host maps a database's
   name, path or identifier to anything.
4. **One file for each installation of the plugin that writes**, holding what that installation
   asserts. No file ever has two writers, so a file synchronizer never has a conflict to settle, and
   copying the newer of each file from one replica of the folder to another is a correct merge
   [M here, 21 checks].
5. **What the store means is the join of those files**, worked out by the plugin when it loads
   them: no modification time and no clock is compared. Where two installations changed the same
   thing without knowing of each other, the answer is the closed one: a revocation any of them
   asserts holds, a profile name taken off stays off, the more refusing standing answer wins, and a
   node whose rule has two versions refuses until the operator settles it.
6. **The store checks itself.** Every file records how far its writer had seen each other writer.
   A file that is missing, changed, renamed, or older than another file or this computer has seen
   closes the database's gate (`503 undecided`) and is named on the tab [M here].
7. **The logical model does not change**: a flat dictionary of `KPG.` keys behind `IKeyValueStore`.
   `AuthorityStore`, `AdmissionStore` and the pending buffer keep working over that seam; what is
   behind it is the store's image instead of `PwDatabase.CustomData`.
8. **No store write touches the database.** A pairing, a renewal, a revocation, a standing answer, a
   profile change and a rule change set no `Modified` flag, need no save and no turn at a dialog's
   mercy. The plugin's own data makes it write the database only when an authority is created,
   recreated or deleted.
9. **The API and the reference client do not change.** No route reaches custom data today and
   none will; nothing of the store is in the tree.

**The honest weaknesses, first.**

- The two keys left in the file are still in the place a KeePassXC merge can empty. Measured
  again here for exactly these keys: a merge into a copy that holds them, from a stale copy that
  does not hold them and holds any other key, removed both, in all four releases [M here, 8 of 8
  cells]. What changes is the consequence: today that merge silently brings back a retired
  authority and revoked clients [M spike]; here the file can no longer open its store, the gate
  closes, and nothing is admitted until a copy that still holds the keys is synchronized in.
- A rule names its node by identifier, so a copy of a node does not carry the node's rule. The
  copy is governed by where it lands. Section 18.
- The store is a second thing to back up and to carry. KeePass's own File then Synchronize carries
  the file and not the folder.
- Sealing the store is a cipher call the plugin makes itself. The authority's key is not under it.

**How this relates to the timestamp-named-field idea** (JSON kept so that a merge preserves both sides, and a
preprocessing step at load that takes care of merge issues). This design is that idea with the
unit that merges moved to where it does merge. Inside the file the unit every program merges is the
whole entry [M keepass 1a; M keepassxc 4; M others 4; R android 1.1], so two sides' fields in one
entry do not both survive. Here each side writes a file of its own, both files always survive, and
the step at load is the join of section 6. That step writes nothing into the database, so "opening
changes nothing in it" (operator answer 3a, [R inventory O1]) holds.

## 2. The three recorded objections, and what this design does with each

| Settled decision (CLAUDE.md) | The objection it recorded | This design |
| --- | --- | --- |
| "Every database is its own certificate authority, stored inside itself", "a malicious `.kdbx` therefore vouches only for itself" | One authority for several databases needs something that decides which database a certificate is for. That something names databases, and a database is a file an attacker can supply, so the file can claim the name (`CA.md` §202, `DESIGN.md` §113 [R plugin]). A stolen key was also a total loss, not one database's (`DESIGN.md` §125) | **Kept whole for the authority**: still one per database, still inside the file, still the database's identifier. What moves out is state, and it is bound by possession of a key the file holds, never by a name. A prepared file brings its own key and its own store, or none: it vouches only for itself. The trust domain is "the database and the folder its key opens" |
| "Do not re-propose a host-level authority or registry" | The registry was the forgeable part: a table on the host keyed by something a file can assert | **Not proposed.** The host keeps three small things, none of which admits anything: this installation's writer name; per store id, how far it has seen each writer, which can only refuse; and optionally, per store id, a folder. All three are keyed by the store id, which is derived from the key. A file without the key can make the plugin look at a folder and fail to open it, nothing more. The file never names a path |
| "The authority key has no passphrase. It is protected by the kdbx encryption." | The deleted draft was cryptography written here (Argon2, AES-CBC, encrypt-then-MAC, DPAPI) over a key that sat beside an encrypted database anyway, claiming a second factor that was none, with a backup that restored nowhere because a DPAPI blob is tied to a machine (`DESIGN.md` §125, `CA.md` §202 [R plugin]) | **Kept for every key that signs.** The authority's key, the host's key and the gate key stay in the file, with no passphrase, and are written nowhere else. The store is sealed with one library call (AES-256-GCM in Bouncy Castle, which the plugin already ships for TLS) under a random key that itself lives in the file: no passphrase, no key derivation, no DPAPI, nothing tied to a machine, so a copy of the folder restores wherever the database does. It is still a cipher the plugin calls itself, which the present design has none of. That is a reversal, for records and rules and not for keys |

What the store key is worth. It opens the records, the names and the rules, which are less than the
database it sits in. Whoever holds it and can also write the folder can forge the store: bring a
revoked client back, widen a rule or a name for a certificate that already exists. They cannot mint
a certificate or serve as the host, because the authority's key is not in the store. To hold the
store key one must have opened the database.

Three of the four things `CA.md` §202 lists as following "for free" from an authority in the
database change: "no external state" is reversed; "backing up the authority is backing up the
database" holds for the authority and not for the clients and rules; "replication works" holds when
the folder travels with the file and not otherwise.

## 3. The variants, and why this one

| Variant | What it would be | Verdict |
| --- | --- | --- |
| **Everything outside** | The authority document sealed in the store too; the file holds only the store key | Rejected. One write-once secret has to stay in the file in any case, to bind the store without a name, so moving the authority out buys no survival: the store key would sit exactly where the authority sits now. It would put the keys that sign on the disk under the plugin's own sealing, which is the thing the third decision deleted, and a gate that has lost its store could no longer answer at all, since the host certificate would be in the store |
| **Only what changes outside** (chosen) | Authority and store key in the file, written once; data 2 to 7 in the store | **Chosen.** Every datum that was measured failing under another program is one that changes or accumulates [M spike; R inventory 3]. The two that stay are write-once, and a write-once key failed only by being removed [M spike; M here] |
| **Rules inside, the rest outside** | Rules stay on their nodes | Rejected. Rules are the datum other programs destroy most: KeePassDroid deletes every rule on a save, KeePassDX 3.2.0 and 3.3.0 every group's, with no time moved, and KeePassDX, KeePassXC and kdbxweb merges leave a stale rule under a newer time [M spike; M others]. A deleted rule widens. Keeping a digest of each rule outside to detect that would make every KeePassDroid save close every ruled node, and the store would then hold the truth anyway |
| **The store in the user's profile only** | `%LOCALAPPDATA%\KeePassGate\stores\<id>` on the gate's computer | Rejected as the default, kept as a choice (section 11). No synchronizer or phone would ever see it, which is its strength. But a second Windows computer would have no clients and no rules, a restored database would have no store unless the profile was backed up too, and a revocation made on one computer would never reach another. Beside the database, whatever carries the file carries the folder |

## 4. What stays in the file

In `PwDatabase.CustomData`, the carrier the authority uses today:

| Key | Value | Written | Changed |
| --- | --- | --- | --- |
| `KPG.Authority.<sha256 of the authority certificate>` | today's document, unchanged: `{"v":"1","ca":…,"host":…,"sniKey":…}`, 2903 characters [M inventory] | Create and Recreate, at the settings dialog's OK | never; removed at Recreate and Delete, as the prune removes a retired document today |
| `KPG.Store.<id>` | `{"v":"1","key":"<base64 of 32 random bytes>"}`; `<id>` is 32 hex digits, the first 16 bytes of SHA-256 over `KPG/1/store/` and the key bytes | with the first thing the store has to hold, and again with every new authority (section 9.4) | never, and never removed by the plugin |

```text
KPG.Store.3f9a61c07d2e4b18a5c2e90f4471bd06 = {"v":"1","key":"q83vEjRWeJASNFZ4kBI0VniQEjRWeJASNFZ4kBI0Vng="}
```

- **The id is derived from the key**, so a key under the wrong name is recognized and ignored, and
  the name alone finds the folder.
- **The value is never empty**, because kdbxweb drops an item whose value is empty on every save
  [M others].
- **A plain string in KeePass's memory**, as the authority document is today, which `CA.md` §208
  already concedes [R inventory P14]. KeePass's XML export writes both in the clear, as today
  [R inventory P26].
- **No per-key time is needed.** Written through KeePass's public setter each key gets one and the
  file becomes KDBX 4.1; written with no time the file stays 4.0 and every KeePass merge still
  kept both keys [M here]. Section 16.
- **No retirement, record, revocation, answer, name or rule is in the file**, and `AclDocument`'s
  key `KPG` on nodes is gone. A program that deletes node custom data deletes nothing of ours.

## 5. The store

### 5.1 Where

```text
D:\Vault\Passwords.kdbx
D:\Vault\Passwords.kdbx.kpg\
    store.json                  clear: which store this is, and a line for a person
    a1b2c3d4e5f60718.kpgw       sealed: what the installation named a1b2… asserts
    0f1e2d3c4b5a6978.kpgw       sealed: the same for a second computer, if there is one
```

- **`store.json`** is `{"v":"1","store":"3f9a61c07d2e4b18a5c2e90f4471bd06","note":"KeePassGate data
  for a KeePass database. Keep this folder beside the database and back it up with it. Its files are
  sealed. Changing or deleting one closes the gate."}`. It is the only clear file and holds nothing
  secret.
- **The folder is found by its id, not its name.** The plugin looks, in the database's folder, at
  each directory whose name ends `.kpg` and reads its `store.json` (at most 1 KiB, at most 64
  directories). The name is for people. A renamed database still finds its store.
- **A writer** is one installation of the plugin: 8 random bytes, written as 16 hex digits, kept as
  `KPG.Writer` in KeePass's configuration beside `KPG.Port` (`Bridge/KeePassSettings.cs`
  [R plugin]).
- **On the computer and not in the folder**: `%LOCALAPPDATA%\KeePassGate\seen\<id>.kpgs`, sealed
  under the same key, holding for each writer the highest count this installation has loaded and
  the digest of that file. It can only make the plugin refuse. A computer that has none takes the
  store as it finds it.
- **Another folder** can be chosen for a store on the Store page, kept as `KPG.StoreFolder.<id>` in
  KeePass's configuration. It is needed when the database is not a local file, when its folder
  cannot be written, and when two computers share a database only through KeePass's own Synchronize
  (section 12). The database never names a folder.

### 5.2 Sealing

A writer file is the five bytes `KPGW1`, a random 12-byte nonce, and the output of AES-256-GCM
(Bouncy Castle's `GcmBlockCipher`, 128-bit tag) over the plain text, with `KPG/1/writer/<store
id>/<writer>` as the associated data. So a file opens only under this store's key and only under
its own name: one bit changed, a file copied under another writer's name, and the right file under
another key were each refused [M here]. A megabyte seals in 8.3 ms and opens in 8.8 ms [M here].
A file over 4 MiB and a folder of more than 16 writer files are refused before any work.

### 5.3 A writer file

The plain text is JSON Lines, every line one flat object of strings, which is what `JsonBody`
already reads (`Transport/JsonBody.cs`, at most 32 members a line [R plugin]) and `JsonWriter`
writes. The first line is the header, every other line one key.

```text
{"v":"1","store":"3f9a61c07d2e4b18a5c2e90f4471bd06","writer":"a1b2c3d4e5f60718","head":"42","saw":"0f1e2d3c4b5a6978:9"}
{"k":"KPG.RetiredAuthority.9c41…","n":"30","saw":"","v":"retired"}
{"k":"KPG.Client.5be0…","n":"36","saw":"0f1e2d3c4b5a6978:7","v":"v 1\nsubject …\nclient …\nadmitted …\nprofiles reader ops\ncertificate MIIB…"}
{"k":"KPG.Revoked.77d2…","n":"40","saw":"0f1e2d3c4b5a6978:9","v":"2027-01-01T00:00:00Z 5d0c…"}
{"k":"KPG.RenewalRefused.5be0…","n":"41","saw":"0f1e2d3c4b5a6978:9","v":"forever"}
{"k":"KPG.Profile.reader","n":"3","saw":"","v":"1"}
{"k":"KPG.Profile.old","n":"17","saw":""}
{"k":"KPG.Rule.4f0c91aa22b7d31e8a6640c5b9e2f7a1","n":"42","saw":"0f1e2d3c4b5a6978:9","v":"{\"default\":\"deny\",\"ops\":[\"read\",\"list\"]}"}
```

| Field | Meaning |
| --- | --- |
| `head` | how many times this writer has written its file. It only grows |
| `saw` (header) | for each other writer, the `head` of that writer's file when this file was written |
| `k`, `v` | the key and its value, the same text the key holds in custom data today. A line with no `v` is a key taken away |
| `n` | this writer's `head` when it wrote the line |
| `saw` (line) | the header's `saw` at that moment: what this writer knew when it decided this key |

A writer holds one line a key at most. It lets a line go when another writer's line for the same
key knows of it, and when the thing it is about has expired (section 9.5).

**Size.** A store of 200 rules, 30 records, 30 revocations and 10 names over three writers is
81,764 bytes in three files [M here].

## 6. What the store means

Loading reads every `<16 hex>.kpgw` in the folder and nothing else, opens each, and works out one
dictionary, the **image**. No time is read anywhere in it.

**One line knows of another** when its `saw` holds the other's writer at a count no lower than the
other's `n`. For one key, every line that another line knows of is dropped. What is left decides:

| Family | One line left | Several lines left, none knowing of another | Taking away |
| --- | --- | --- | --- |
| `KPG.RetiredAuthority.<fp>`, `KPG.Revoked.<fp>`, `KPG.Closed`, `KPG.Succeeds` | held | **held while any writer asserts it.** Knowing does not enter into it | a writer can only stop asserting its own line. Nobody can take another writer's away |
| `KPG.Client.<fp>` | its value | an emptied one wins; equal values are that value; otherwise empty | as above: only the writer's own line |
| `KPG.RenewalRefused.<fp>` | its value, or lifted | the more refusing: `forever`, else the later moment, else lifted | a line with no `v`, so a lift travels, which it does not today [R inventory D5] |
| `KPG.Profile.<name>` | on, or off | **off** if any of them takes it off | a line with no `v` |
| `KPG.Rule.<node>` | its document, or none | **the node refuses**: the image holds a text that does not read as a rule, which `AclResolver` already answers by refusing there and below | a line with no `v` |

Measured on the prototype, two folders and a copy of the newer of each file between them standing
for a synchronizer [M here]: a revocation made on one installation while the other was behind held
on both once the folders met, and could not be taken away from the other; a name added on one and
taken off on the other, neither knowing, was off, and on again once added by somebody who knew of
the removal; `forever` beat a lift made at the same time; a rule changed on both refused on both,
was settled by one write on one of them, and a later change that knew of the settled one took
over.

**The checks made before the image is believed.** Any failure leaves the database `503 undecided`
and names the writer on the Store page:

1. Every `.kpgw` file opens under the key and under its own name.
2. No file is older than another file saw it: for every writer W named in any `saw`, W's file is
   present and its `head` is no lower.
3. No file is older than this installation saw it: the same against the seen file, digests
   compared where the counts are equal.

Measured [M here]: a writer's file put back as it was one write earlier, and a writer's file
deleted, each closed the store, both through the seen file and, on an installation with no seen
file, through what the other writer's file recorded; putting the right file back opened it again.

What these checks cannot see is in section 18: a folder taken back whole to an earlier state on a
computer that has no seen file.

## 7. Each datum: where it lives, and in what shape

| # | Datum | Where | Shape |
| --- | --- | --- | --- |
| 1 | The authority: certificate, key, host PKCS#12, gate key | **the file**, `KPG.Authority.<fp>` | unchanged |
| 1a | The store key (new) | **the file**, `KPG.Store.<id>` | section 4 |
| 2 | A retirement | store, `KPG.RetiredAuthority.<fp>` | `retired`. Never let go; carried into every later store (section 9.4) |
| 3 | An admission record | store, `KPG.Client.<fp>` | today's record text (`AdmissionRecord.cs` [R plugin]). Written by the installation that issued the certificate, in the same file write as any new profile name; emptied at a revocation |
| 4 | A revocation | store, `KPG.Revoked.<fp>` | today's value, `<notAfter> <authority>`. Held while any writer asserts it; a writer lets its own go only once the certificate has expired |
| 5 | A standing answer to renewals | store, `KPG.RenewalRefused.<fp>` | `forever` or a moment; a lift is a line with no value |
| 6 | The profile names | store, one key a name, `KPG.Profile.<name>` | `1`. At most 32 held; more than 32 after a join reads as a list that will not read, which names nothing, as today |
| 7 | An access rule | store, `KPG.Rule.<32 hex digits of the node's UUID>` | the rule document byte for byte as `AclDocument.Write` produces it today |
| 8 | Recycle bin markers (T049, optional) | store, `KPG.RecycleBin.<group>.<since>` | keys only ever added, if that design wants them. Nothing here needs them |

## 8. The read path

- **`RoutedStore : IKeyValueStore`** stands where `new CustomDataStore(database)` stands today (12
  places in `OpenDatabase`, `OpenDatabaseTargets`, `DatabaseWriter` and `AuthorityChooser`
  [R plugin]). Keys beginning `KPG.Authority.` and `KPG.Store.` go to `CustomDataStore`; every
  other key goes to the image. `AuthorityStore`, `AdmissionStore`, `RenewalRefusal` and the readers
  above them see the dictionary they see today.
- **On every request** the plugin asks the folder for the names, lengths and times of its `.kpgw`
  files and compares them with what the image was built from: 50 microseconds [M here, local
  NTFS]. If anything differs it loads again: 1.9 ms for the store of section 5.3's size [M here].
  The files' own times are compared and not the folder's, because a file written in place moved its
  own time and not the folder's [M here]. So a revocation or a rule made on another installation
  bites on the first request after its file lands, and one made here bites at once, because a write
  swaps the image.
- **A rule**: `KdbxNodes.DocumentOf` (`Bridge/KdbxNodes.cs:65-73` [R plugin]) asks the image for
  `KPG.Rule.<uuid>` where it asks the node's custom data today. One lookup is 26 ns [M here].
  `IAclNode`, `AclResolver`, nearest wins per profile, the empty array, `default` and
  `AclDocuments` do not change.
- **The profile names**: `ProfileNames` lists the keys beginning `KPG.Profile.` where it parses one
  value today.
- **The image is an immutable object swapped whole**, so reading it needs no turn on KeePass's
  thread. The tree walk still holds that thread still, as today.
- **At a lock** the store key and the image are let go with the authority's keys, and the key's
  bytes are overwritten. The responder answers `503 locked` as today; it needs nothing from the
  store.

## 9. The write path

### 9.1 One writer, one file write

`SidecarStore.Apply(changes)` is the only thing that writes the folder. Under a mutex named for the
store and the writer (so a second KeePass on the same computer waits): look at the folder again and
load if it moved; refuse if the store is not in a state to be written; raise `head`; write the
changed lines with the present `saw`; drop this writer's lines that another writer's line knows of;
seal; write `~<writer>.tmp` in the folder; flush it to the disk; replace the writer's file with it;
read the file back and compare its digest; write the seen file; swap the image.

It took 18.6 ms with both loads [M here]. A replace is one step, so the folder holds the old file
or the new one and never half of either. A replace was refused while another program held the
file open without sharing deletion, and went through when the reader shared it [M here, section
20], so a refused replace is retried for a bounded time and then fails as not saved, with nothing
changed.

### 9.2 A pairing, a renewal, a revocation, a standing answer

`DatabaseWriter` keeps its turn on KeePass's thread for what it compares there today (the database
still open and unlocked, the authority the approval was made against, the record a renewal renews
[R inventory P13, decisions]). What it then does is `Apply`, where today it sets custom data, saves
through `MainForm.SaveDatabase`, waits, checks `Modified`, hashes the file back and takes the write
out again if the save did not land.

- **No credential before its record is durable** still holds: the certificate is handed over after
  the file has been replaced, flushed and read back.
- The record and a newly approved profile name are lines of one file write, as they are keys of one
  save today.
- `DELETE /gate/certificate` writes the revocation and empties the record in one file write.
- A standing answer is written at once. **Owed answers go**: there is no save to wait for, so
  `IOwedAnswers`, `OwedAnswerSaves` and the write at `FileSavingPre` have nothing left to do.
- `500 not verified` goes for these writes: the replace either happened or did not.
- KeePass busy with a save, a dialog open on the database's custom data, an unsaved database, an
  operator answering "Discard changes": none of them can lose or delay a record any more, because
  the record is not in what they are about.

### 9.3 The settings tab

Everything on the KPG tab still waits for the dialog's OK (`PendingDatabase`, `PendingStore`,
`AuthorityPanelFactory` on `FormClosed` [R plugin]). The buffer is replayed key by key as today,
into `RoutedStore`: the store's keys as one `Apply`, the file's keys as section 9.4 describes.

- The reconciler that replays `KPG.Profiles` as a difference goes: with one key a name, key by key
  is the difference.
- The reconcilers for `KPG.RenewalRefused.` and `KPG.Revoked.` stay as they are. They are about a
  second writer in the same process, the renewal dialog and a client revoking itself, and that has
  not changed.
- Revocation still waits for OK, as argued and accepted.

### 9.4 Create, Recreate, Delete: the only writes to the database

These are the only operations that write the file, and they have to be saved before the store is
touched, or a discarded save would leave a store nobody holds the key to. So their OK saves the
database the way a pairing saves it today (`DatabaseWriter`, save, `Modified` false, the file
hashed back), and does nothing if that fails.

- **Create and Recreate**: make a new store key and a new authority; write `KPG.Store.<new id>` and
  `KPG.Authority.<new fp>`, remove the old authority's document; save and verify. Then make the new
  folder's first writer file holding every rule, every profile name and every retirement the old
  image held, the retirement of the old authority among them, and `KPG.Succeeds` naming the old
  store. Then write `KPG.Closed`, naming the new store, into the old one. Records, revocations and
  standing answers are not carried: they die with their authority, as `RemoveAll` does today
  [R inventory P21].
- **Delete**: write the retirement into the store, remove the authority's document from the file,
  save. The store key and the store stay, so rules and names outlive the authority, as they do
  today.
- **Why a new store key with every new authority.** Recreate is the documented answer to a leaked
  copy of the unlocked database (`PAIRING.md` §301 [R plugin]), and such a copy holds the store key
  as surely as the authority's. One rule, one code path.
- **Which store a file means.** Every `KPG.Store.<id>` key in the file is tried. A store that does
  not open, or whose image holds `KPG.Closed`, or that another open store names in `KPG.Succeeds`,
  is not a candidate. One candidate: that is the store. None, with a key present: `503 undecided`,
  store missing. Several: `503 undecided` until the operator keeps one on the Store page, which
  closes the others. So an old key that a stale copy brings back, or that was never removed, costs
  nothing, and an old store somebody refills is ignored while its successor reads.
- **An old store keeps its rules and names** after it is closed. If the newer key is ever lost
  from every copy of the file, the older key still opens them, and a new Create carries them.

### 9.5 The grant editor

The rule is no longer the node's, so no dialog copies it and no dialog's OK can undo it.

- `IGrantStore` keeps its two methods (`Ui/IGrantStore.cs` [R plugin]). Behind it is a buffer of
  the tab's own: `Read` answers the buffer, else the image; `Write` changes the buffer.
- **OK**: a `FormClosed` handler applies the buffer when `DialogResult` is `OK`, as
  `AuthorityPanelFactory` does for the settings tab, with one `Apply`. **Cancel** drops it.
- **The database is not marked modified** for a rule: nothing in it changed. A rule change makes no
  revision of the entry and moves no node's time, so it can never outrank an edit made in another
  copy, and restoring a revision can no longer bring an old rule back [R inventory 1.2].
- **Closing the entry dialog with only a rule changed.** KeePass asks whether to save when its own
  entry changed (`PwEntryForm.cs:2146-2185` [R 2.62]) and cannot see the tab's buffer, so the tab
  asks the same question from its own `FormClosing` when the buffer holds a change and KeePass did
  not ask. The group dialog has no such question today.
- **Add Entry and Add Group**: the new entry has its UUID before the dialog opens and is added at
  OK (`MainForm_Functions.cs:6772`, `:6812` [R 2.62]); a new group is in the tree before the dialog
  opens and taken out on Cancel (`MainForm.cs:1367-1378` [R 2.62]). A rule chosen there is applied
  at OK like any other.
- **Two versions.** The tab shows both rules and one line, "This rule was changed on two computers.
  The gate refuses here until one is kept." OK writes the rules shown, which knows of both.
- **What goes**: `KeePassContract.NodeFormCustomDataField`, `NodeDialog.Working`, the read-only
  state for a KeePass that renamed `m_sdCustomData`, and the test pinning that field.

### 9.6 Letting go

Pruning rides on this installation's own store writes and never on opening. A writer lets go of its
own record and its own revocation once the certificate has expired, with no line left behind: a
record that came back would be refused on its dates. It never writes a line to take away another
writer's record. Rules for nodes that no longer exist stay until the operator removes them on the
Store page, because the node may still exist in a copy that has not been merged. Nothing in the
database is ever written by a prune, so operator answer 3a holds without an exception.

## 10. What changes in the plugin

| Class | Change |
| --- | --- |
| new `Store/SidecarStore`, `StoreImage`, `WriterFile`, `StoreSeal`, `StoreSeen`, `StoreLocation` | the folder, the join, the checks, the one writer, the seen file, finding the folder |
| new `Bridge/RoutedStore` | `IKeyValueStore` over the file's two families and the image |
| `CustomDataStore` | stays, for `KPG.Authority.` and `KPG.Store.` only |
| `AuthorityStore`, `AuthorityService`, `AuthorityDocument`, `AuthorityKeys` | unchanged over the seam. `AuthorityKeys` gains the store key's name. Retirements arrive from the image. The prune of a retired document stays |
| `AdmissionStore`, `AdmissionRecord`, `RenewalRefusal` | unchanged over the seam. The prunes become "let go of this writer's own" |
| `ProfileNames` | reads and writes one key a name; `Merge` goes |
| `AclDocument` | `CustomDataKey` goes; format, reader, writer and ceilings stay |
| `AclResolver`, `AclGrant`, `AclVerb`, `AclDocuments`, `VerbMask` | unchanged |
| `KdbxNodes`, `DatabaseNodes`, `IRuledNodes` | `DocumentOf` asks the image; the ruled nodes are enumerated from the image's `KPG.Rule.` keys |
| `DatabaseWriter` | the admission, revocation and standing-answer paths lose the save, the verification and the taking back out, and call `Apply`. The vault's writes to nodes keep all of it. The save-and-verify path gains Create, Recreate and Delete as callers |
| `IOwedAnswers`, `OwedAnswerSaves`, `OwedAnswersAtExit` | go |
| `ApprovalBook` | stays. An approval is still held when no turn on KeePass's thread comes; the dialogs and saves that used to cause it no longer do |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | replay into `RoutedStore`; the profile list's reconciler goes; a Store page's buttons are added |
| `NodeDialog`, `IGrantStore`, `GrantPanelFactory` | section 9.5 |
| `GroupDialogs` | stops guarding the plugin's own data: Database Maintenance's copy of the custom data can hold only the two write-once families, which are written from a modal dialog it cannot be open under. It stays for the vault's writes |
| `OpenDatabaseTargets` | answers `503 undecided` for the store's states of section 11; the per-request look |
| `MainWindowDatabases` | a Save As to another local folder copies the store's files beside the new file |
| `KeePassSettings` | `KPG.Writer`, `KPG.StoreFolder.<id>` |
| KPG settings tab | a fourth page, **Store**: where the folder is, its state in one line, the writers and when each last wrote, and the buttons of section 11 |
| Tests | `SnapshotHazardTests` and the rule cells of `MergeInRealDatabaseTests` give way to: the join's cases (section 20's are a start), the sealed file's refusals, and a host test pinning both file keys under every KeePass merge method. `CustomDataMergeRuleTests` stays, because the two keys still rest on it |

## 11. Finding the store, and the states it can be in

The plugin looks for a store id's folder in the folder `KPG.StoreFolder.<id>` names, if set, and
otherwise beside the database. A database that is not a local file has no "beside", so its first
Create asks for a folder and offers `%LOCALAPPDATA%\KeePassGate\stores`.

| State | The gate answers | The Store page shows | The way out |
| --- | --- | --- | --- |
| No store key and no authority | nothing, as a database with no authority today | "No store yet" | Create |
| Store found, opens, checks pass | serves | folder, writers, last write | |
| Authority in the file, no store key, or a key and no folder | `503 undecided`, with the authority's host certificate as `undecided` is answered today | "The store folder for this database is not beside it" and where it was looked for | put the folder back, or choose where it is, or Recreate. A key deleted by hand while the database stays open can be put back from the page, because the plugin still holds it |
| A `.kpg` folder beside the database that no key in the file opens | as the row above | "This database holds no key for the store beside it" | synchronize in KeePass with a copy that holds the key (section 13), or Recreate |
| A writer's file does not open, is missing, or is older than another file saw | `503 undecided` | the writer and the reason | put that file back from the other computer or a backup, or Recreate |
| The store is older than this computer saw | `503 undecided` | "This store is older than this computer last saw (writer, count seen, count found). Using it can bring back clients revoked since." | put the newer files back, or **Use it as it is**, the operator's press, which rewrites the seen file |
| This writer's own file is not the file it wrote | `503 undecided` | "Another installation is writing under this one's name" | **Take a new name**, which writes this installation's lines again under a new writer name |
| Two stores open, neither succeeding the other | `503 undecided` | both, with their writers and dates | **Keep this store**, which closes the other |
| More than one authority not retired | `503 undecided`, as today | as today | as today |

`503 undecided` already means "the operator has to decide something in KeePass" to a client, and
the reference client already waits on it (`retry_undecided_timeout`, `src/pykeepassgate/client.py`
[R plugin]). No new word is needed on the wire.

## 12. Backup, restore, a second computer, a renamed or copied database, a synchronized folder

| Event | What happens |
| --- | --- |
| **Backup** of the database's folder | takes the store with it. A backup of the `.kdbx` alone takes the authority and the store key and none of the clients or rules |
| **Restore both** on the same computer, older than it last saw | `503 undecided`, "older than this computer last saw". The operator presses Use it as it is. Today the same restore brings revoked clients back with no question asked |
| **Restore both** on a new computer | served as found. There is nothing to compare with |
| **Restore the `.kdbx` only**, older, beside the present store | served: the store is the truth, and rules for nodes the older file lacks apply to nothing |
| **Restore the `.kdbx` only**, the store lost | `503 undecided`. Recreate: every client pairs again and every rule is made again. This is the cost of not backing up the folder, and it is closed |
| **Restore the store only**, older | `503 undecided`, as above |
| **Rename the database** | nothing: the folder is found by its id. Its name stays the old one until a person renames it |
| **Copy the database** in the same folder | both files hold the key and find the one store: two copies of one database, sharing an authority as today, refused while both are open [R inventory P27] |
| **Copy or move the database alone** to another folder | `503 undecided` there until the folder is copied too or pointed at |
| **Save As** in KeePass to another local folder | the plugin copies the store's files beside the new file when the save lands. Save a copy, and a copy made outside KeePass, do not carry it: KeePass tells a plugin that a save is a copy and not where it went (`FileSavingEventArgs`, `KeePass/Forms/_Accessories.cs:313-337` [R 2.62]) |
| **A second Windows computer, folder synchronized** | it writes a file of its own. Both serve the same clients under the same rules, as `DESIGN.md` §113 promises today. A change made on one reaches the other when its file does |
| **A second Windows computer, sharing only through File then Synchronize** | it has the authority and the store key and no folder: `503 undecided`. KeePass announces a synchronize to a plugin with the database alone, never the other file (`ImportUtil.cs:344-371` [R 2.62]), so the plugin cannot carry the folder itself. The operator puts the store in a folder both computers reach and points each at it, or copies the folder by hand, and then a revocation made on one reaches the other only when the folders next meet. Section 18 |
| **A synchronized folder** | each file has one writer, so a synchronizer has nothing to merge and makes no conflict copy [A, from Syncthing's and Dropbox's documented rule that a conflict is two devices changing one file, R others]. A conflict copy that does appear has a name the plugin does not read. A file that is only a placeholder and cannot be fetched does not open: `503 undecided` |
| **A phone** | a synchronizer that carries the whole folder puts sealed files on the phone that nothing there reads. A program that fetches the `.kdbx` alone never sees the store [A] |
| **KeePass's own save** | writes `<path>.tmp` beside the database and replaces the file (`FileTransactionEx.cs:51`, `:130-136` [R 2.62]); it cleans up only files named `KeePass_TxF_*.tmp` (`:520-533` [R 2.62]). A folder ending `.kpg` matches neither |

## 13. What a save and a merge do to it, program by program

The store itself: **no program that opens a `.kdbx` reads, writes or merges it** [A for each; none
of the merges read in the ground reports opens anything but the two database files]. So every row
below is about the two keys in the file, both written once and never changed.

| Program | A save | A merge | What the gate then does |
| --- | --- | --- | --- |
| KeePass 2.62 | both keys kept; with their times the file is 4.1, without them 4.0 [M here] | 40 cells, five methods, both directions, with and without times: in all 30 whose open copy held the keys both stayed, unchanged; a copy that lacked them received them in 8 of 10, the other 2 being Keep existing, which copies nothing [M here]. No method removes a key [M spike, 70 of 70] | serves. A copy that lost the keys elsewhere gets them back by synchronizing with one that holds them |
| KeePass, Database Maintenance | the Plugin Data tab lets a person delete a key by hand [R inventory A9] | | the plugin still holds the key while the database stays open and offers to put it back; after that, `503 undecided` |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | both keys kept, per-key times dropped, file 4.0, `_LAST_MODIFIED` added, and `KPXC_RANDOM_SLUG` from 2.7.12 [M here, 4 of 4; M spike] | **both copies hold the keys: kept**, 12 of 12 cells. A stale copy whose custom data is empty: the target's keys kept, 8 of 8. Into a copy that lacks them: they arrive, 8 of 8. **Into a copy that holds them, from a stale copy that lacks them and holds another key: both removed, 8 of 8** [M here, slow KDF]. That is the replacement the spike measured [M spike] | in the last case the file opens no store: `503 undecided`, nobody admitted. Never an older state served |
| Keepass2Android 1.15-r6, 1.08d-r5 | every key kept, per-key times lost [M spike] | a key only one copy holds is in the result, 18 of 18; a key both hold takes one copy's value by `SettingsChanged` [M spike], and the two values are the same here | serves |
| KeePassDX 4.5.8, 3.2.0, 3.3.0 | every key kept with its time [M spike] | by per-key time [M spike; R android 3.3]; same value either way. It deletes group custom data (3.2.0, 3.3.0) [M spike], which holds nothing of ours | serves |
| KeePassDroid 2.7 | every database key kept; all node custom data deleted [M spike], which holds nothing of ours | has no merge [M spike] | serves |
| kdbxweb 2.1.1 (KeeWeb) | values kept, an empty value dropped [M others], and ours are never empty. A 4.1 file whose database custom data carries a time comes out unreadable to KeePass [M others]: that hits the present layout and this one alike unless the two keys are written without times (section 16) | takes an arriving key only when both carry times and the arriving one is later, or the open copy lacks it and does not list it as deleted [R others]. Whether it ever removes a key the open copy holds was not read | serves, or the file is unreadable to KeePass altogether, as today |
| Strongbox, KeePassKit, KeePassium, pykeepass, gokeepasslib | carry database custom data [R others; M others for pykeepass] | Strongbox and KeePassKit merge it as KeePass does, removing nothing [R others]; keepass-rs merges no metadata [R others], so the open copy's keys stay [A]; the others have no merge [R others] | serves |
| One whole file written over another: Keepass2Android's "No, overwrite", a KeePassDX save within ten seconds, any KeePassDroid save over a changed file, a synchronizer keeping one of two files [R android 5.5; M spike; R others] | | nothing merges | if the surviving file holds the keys, nothing is lost: **a pairing, a revocation and a rule made in between are in the store and not in the file that was overwritten.** If it is a copy from before the keys, `503 undecided` |

What broke the present store does not reach this one:

| Measured failure today | Here |
| --- | --- |
| KeePassXC merge from 2.4.2 undoes a revocation and a retirement [M spike] | the revocation and the retirement are not in the file. The merge can remove the two keys, which closes the gate |
| Keepass2Android merge brings back a stale record and a stale profile list [M spike] | neither is in the file |
| KeePassDroid deletes every rule, KeePassDX 3.2.0 and 3.3.0 every group's, no time moved [M spike] | no rule is on a node |
| KeePassDX, KeePassXC and kdbxweb merges leave a stale rule under a newer time [M spike; M others] | no rule is merged by any program |
| KeePass synchronize takes one copy's profile list whole [R inventory D6] | names are joined one by one, and a removal wins |
| A lift of a standing answer does not travel [R inventory D5] | it is a line, and travels |
| Database Maintenance's copy undoes a write made under it [M spike] | the plugin writes no custom data while it can be open |

## 14. What a person sees, what they can break by hand, and what the gate does

| Where | What is seen | What can be broken | What the gate does |
| --- | --- | --- | --- |
| KeePass | nothing in the tree. Two kinds of key under Tools, Database Maintenance, Plugin Data. A Store page on the KPG tab. The KPG tab on groups and entries as today | deleting a key by hand | section 13 |
| KeePassXC, the Android programs, KeePassium, Strongbox | nothing: no entry, no group, no field, no attachment, no node custom data [A that none lists the database's own custom data to a user; KeePassXC's Properties pages list a node's custom data, R keepassxc 8, and there is none] | nothing of ours on any node | nothing |
| A file manager, a synchronizer's folder list | a folder `Passwords.kdbx.kpg` with one small readable file and one sealed file a computer | renaming the folder | nothing, as long as the name still ends `.kpg` |
| | | deleting or moving the folder | `503 undecided`; putting it back restores everything |
| | | deleting one writer's file | `503 undecided`, named, unless it was the only file on a computer with no seen file: then the store is empty, nobody is admitted, and the Store page shows a store with no writers |
| | | editing a file, or putting an older one back | `503 undecided` |
| | | copying the database somewhere without the folder | `503 undecided` there |
| | | deleting `store.json` | the folder is not found: `503 undecided`; the Store page can be pointed at it, and writes the file again |

Nothing in any row widens. Each either changes nothing or closes the database's gate until a person
acts in KeePass.

## 15. Keeping the API and the reference client out

- **No route reads or writes custom data today** [R inventory P15], and the store key is custom
  data. Nothing has to be hidden, because nothing of the store is a group, an entry, a field or an
  attachment. An agent holding `create` and `update` everywhere has nothing to write that the store
  would read.
- **No route writes the store.** `Apply` is called from the pairing and renewal handlers, a
  client's own revocation, the tabs and nowhere else. The store's folder is not something a route
  can name.
- **The routing document** changes in one place: the description of `undecided` gains the store's
  states. No new status, header or body.
- **PyKeePassGate**: no change to `api`, `cli`, `model`, `refs` or `testing`. A client that asks
  what it may do at a node gets the answer from the same resolver.
- **A node's tag** is made with a key derived from the host PKCS#12 as the database stores it
  [R inventory 1.3], which does not change, so no tag changes.

## 16. The KDBX version it needs

KDBX 4.0. Nothing here is a 4.1 feature. The file is 4.1 today only because KeePass's public setter
stamps a time on every key of the database's custom data [R inventory P12, P17]. If the plugin
writes its two keys through KeePass's internal setter with no time, the file stays 4.0, and every
KeePass merge method still kept both keys in both directions [M here, the `anchored-notimes`
cells]. That would take the plugin's databases out of kdbxweb's defect [M others] and reverse "the
authority takes the file to KDBX 4.1". It costs one more pinned internal member of KeePass, with
the public setter and a 4.1 file as the fallback, which is where things stand today. I recommend
it; the design does not depend on it.

## 17. Settled decisions: reversed and kept

**Reversed, each to be recorded as a reversal:**

| Decision in CLAUDE.md | What replaces it |
| --- | --- |
| "stored inside itself under `KPG` keys in the database's own `CustomData`", for everything but the authority | the store beside the database. The authority and the store key stay |
| "The database's own `CustomData`, never the root group's", with `KPG.Revoked.<key>`, `KPG.RetiredAuthority.<fp>` and "what has to survive a merge is a key that is only ever added" | the same keys, in the store's image. "Only ever added" becomes "held while any writer asserts it", and the merge is the plugin's own |
| "The authority takes the file to KDBX 4.1" | not needed (section 16) |
| "The access control model is one document per node … Grants live under a `KPG` key" in node custom data | one document per node, held in the store under the node's UUID |
| "The grant editor writes into the dialog's own copy, `m_sdCustomData`" | a buffer applied at the dialog's OK; the reflection goes |
| "`KPG.Profiles` is replayed as a difference" | one key a name; the difference is the key |
| Owed standing answers written by every save (ruling of 2026-11-04) | nothing is owed: an answer is written at once |
| "Nothing here is ever written to the disk" (`CA.md` §222), for records and rules | sealed files on the disk. Still true of every key that signs |
| "No external state", and `DESIGN.md` §131's "`CustomData` is the wrong store and that decision has to reopen" | this is that reopening |
| The trap section's `GroupForm` and `DatabaseOperationsForm` hazards, for the plugin's own data | no dialog holds a copy of anything the plugin writes while that dialog can be open |

**Kept:**

- Every database is its own certificate authority, in the file. No host-level authority, no
  registry, no `kpg://any`.
- The authority key has no passphrase and is protected by the kdbx encryption alone.
- The admission record is load bearing: read on every request, profiles from the record less the
  names the list lacks, a lost record leaves the certificate inert. "In the database" reads "in the
  database's store".
- Pairing tokens and held approvals in memory only; the approval dialogs unregistered; the proofs;
  two host certificates; the transport; spec-first routing; the JSON grammar and the one escaper.
- Everything on the KPG settings tab waits for OK, revocation included; on the group and entry
  dialogs OK keeps a rule and Cancel discards it.
- Nearest wins per profile; an empty array is a rule; a document that will not read refuses; no
  per-subject opt-in; the profile list decides what a client holds.
- More than one authority not retired serves nothing; the operator is asked.
- Opening or reading a database changes nothing in it. Here that has no exception left.
- Fail closed; the caller in the threat model; local code execution conceded.

## 18. What it does not solve

1. **The two keys in the file can still be removed by a KeePassXC merge** from a stale copy
   [M here]. The result is closed, and a KeePass synchronize with any copy that holds them brings
   them back [M here]. If no copy holds the store key any more, the rules and names sealed under
   it cannot be read again, except those an older store key in the file still opens (section 9.4).
   The window is a copy made before the first Create, or before the latest Recreate, merged in by
   KeePassXC and then written over every other copy.
2. **A copy of a node does not carry its rule.** Duplicate an entry whose rule narrows what its
   group grants, in any program, and the copy is governed by its group. An import with new
   identifiers, which KeePass preselects, duplicates the whole arriving tree at the root
   [M keepass 2.4; R keepass], where it gets what the root grants. A move keeps the rule. This is
   how a copied file takes the permissions of where it lands, and it is a real loss against rules on
   nodes, where the copy carried the rule. The gate cannot see a copy. The answer is a habit and a
   sentence in `docs/`: narrow at the group, and grant nothing at the root.
3. **The store travels with the folder and not with KeePass's Synchronize.** Two gate computers
   that share a database only through File then Synchronize need a folder both reach. Until the
   folders meet, a revocation made on one is unknown to the other, exactly as an unsynchronized
   copy of the file is today, but through a second channel a person can forget.
4. **A new node that arrives before its rule.** With two gate computers, a node made and narrowed
   on one can reach the other in the file before its rule arrives in the folder, and is governed by
   its group for that gap. A rule changed on an existing node has no such gap beyond the one a
   file's own travel has today.
5. **A whole folder taken back to an earlier state on a computer with no seen file** is served as
   found. That is restoring a backup, and the present design serves a restored file the same way.
6. **A leaked store key and write access to the folder.** Somebody who once held the unlocked
   database and can write the folder can forge the store until the operator recreates. After
   Recreate they can still refill the old store, which is ignored while the new one reads; it is
   served only by a file that has lost the new keys, on a computer that never saw the store
   closed. Today the same person can do nothing without the master key, because records are inside
   the file. This corner is worse than today.
7. **A rule change made on a second computer after another computer's Recreate, before that
   Recreate arrived**, lands in the closed store. The Store page counts such lines; the operator
   makes the change again.
8. **Availability now depends on a folder.** A placeholder that cannot be fetched, a synchronizer
   or a scanner holding a file, a full disk, a share that is away: each closes the gate or fails a
   write. None admits anything.
9. **Sealing is a cipher call of the plugin's own**, with the care that takes: a fresh random nonce
   for every write, the associated data, a test that pins both.
10. **Code running as the user** can delete the folder and the seen file. Conceded, as deleting the
    database is.
11. **Other programs' whole-file overwrites, clocks and merge rules for entries and groups** are
    untouched by this design. They stop mattering to KPG data; they still matter to the operator's
    own.

## 19. A migration, in one paragraph

A database written by 0.1.0 holds everything in custom data and rules on nodes. Carrying it over
would be one operator's press on the tab while the database is open and unlocked: make a store key,
save and verify, write one writer file holding the retirements, records, revocations, standing
answers, each profile name and, from a walk of the tree, each node's rule under its UUID, then
remove the old keys from the custom data and from the nodes and save again. The authority document
stays where it is, so no client pairs again and no tag changes. What would need a decision is a
copy of the file that has not been carried over meeting one that has: its old keys would arrive
again through a merge, and the plugin would have to ignore them without reading them as a second
store. Under the standing rule that an older stored shape is not read or migrated [R inventory O7],
the alternative is Recreate.

## 20. What was run for this report

All from a console, no window, invented content, in
`spikes\storage\probes\outside-file\design\`:
`probe\Kp.cs` and `probe\Store.cs` (one program, compiled by the framework's `csc` against
`KeePass.exe` 2.62 from the review4 worktree and the plugin's own Bouncy Castle 2.7.0 and
Newtonsoft.Json 13.0.4), `probe\Run-Xc.ps1`, `probe\Run-Xc2.ps1`; outputs under `out\`.

| Run | What | Result | Output |
| --- | --- | --- | --- |
| The two keys under KeePassLib 2.62 | four copies of one database: `old` (no keys), `anchored` (both keys, with times, 4.1), `anchored2` (the same after another entry was added), `anchored-notimes` (both keys with their times removed, 4.0). Eight ordered pairs, five merge methods, each result saved and read back | 40 cells. Both keys present and unchanged in 38; the 2 others are `old` as the open copy under Keep existing, which copies nothing. Never removed, never changed | `out\kpmerge.txt` |
| The two keys under `keepassxc-cli` | the releases the `keepassxc` ground report downloaded and verified against the spike's pins. Argon2d, 200 iterations, 64 MiB, 2 lanes, so that the two loads of a merge fall in different seconds. One save and six merges a release | 28 cells: both keys present and unchanged in all. Per-key times dropped by every save | `out\xc.txt` |
| The same, with a stale copy that holds another key | `old-other`: `old` with `Other.Plugin=1` in its custom data. Three merges a release | 12 cells. Into `anchored` and into `anchored-notimes` from `old-other`: both keys removed, 8 of 8. Into `old-other` from `anchored`: both keys arrive and `Other.Plugin` is removed, 4 of 4 | `out\xc2.txt` |
| The store's join | a prototype of sections 5 and 6: sealed writer files, the join, the checks, the write; two folders, and a copy of the newer of each writer's file standing for a synchronizer | 21 of 21 checks: one writer; a revocation made while the other was behind; a revocation that cannot be taken away from elsewhere; two versions of a rule, settled, then superseded; names added and removed at once; standing answers; a file put back older, deleted, changed by a bit, renamed, and opened with another key | `out\scenarios.txt` |
| The store's cost | three writer files, 270 keys, 81,764 bytes | load and join 1.9 ms; the per-request look 50 microseconds; a lookup 26 ns; a write with both loads 18.6 ms; a replace moves the folder's time, a write in place moves only the file's; AES-256-GCM over 1 MiB 8.3 ms and 8.8 ms | `out\timing.txt` |
| A replace under a reader | `File.Replace` while another handle holds the file | refused when the reader does not share deletion, done when it does. Run under PowerShell 7 on .NET 11, not .NET Framework; that the framework's call behaves the same is [A] | `out\replace.txt` |

Not run: anything with a window, any Android program, any file synchronizer, any test tier.

## 21. Measurements to make before this design could be chosen

Most important first.

1. **Real synchronizers over the store folder, two gate computers.** Syncthing, Dropbox, OneDrive
   with Files On-Demand, Nextcloud: is a replaced writer file ever seen half written; is a conflict
   copy ever made when each file has one writer; what a deletion on one side does; how long a file
   takes to arrive; what a placeholder answers to a read. Everything in section 12's synchronized
   rows is [A] until then.
2. **The two keys through the merges not yet run on them**: KeePassXC's merge from its window, its
   reload of a file changed on disk and 2.8.0's remote sync; Keepass2Android's and KeePassDX's
   merges on the emulator with a copy that lacks the keys in each position (the spike's flows, new
   bases). The spike measured keys only ever added; these two are that, but were not the keys run.
3. **A KeePass synchronize after a KeePassXC merge that removed the keys**, through KeePass's
   window, to confirm the recovery section 13 rests on, measured here only through KeePassLib.
4. **The save from the settings dialog's close** at Create and Recreate, in a real KeePass: that
   `DatabaseWriter`'s save and verification can run from `FormClosed`, with triggers and a
   changed-on-disk file.
5. **The grant editor's buffer on the real dialogs**: OK, Cancel, Escape with only a rule changed,
   Add Entry, Add Group, several entries edited together.
6. **The replace on what a database really sits on**: .NET Framework 4.8's `File.Replace` on NTFS,
   exFAT and FAT32 sticks and SMB shares, under Defender and under a synchronizer's reader; how
   long a bounded retry has to be.
7. **The per-request look on a network share**, where 50 microseconds will not hold.
8. **How often a copy escapes a narrowing** (section 18, point 2): KeePass's Duplicate Entry, copy
   and paste, an import with new identifiers and KeePassXC's and the Android programs' duplicate
   commands, against the rule layouts `docs/access-control.md` recommends.
9. **Whether a plugin can learn the other file of a synchronize**, through
   `IOConnection.IOAccessPre` or otherwise, so that the plugin could carry the store when KeePass
   carries the file. If it can, point 3 of section 18 shrinks.
10. **Writing the two keys with no time through KeePass's internal setter** in the plugin, pinned
    by a contract test, and kdbxweb opening and saving the 4.0 file that results.
11. **A store that has lived**: thousands of lines, a writer that never came back, many Recreates;
    the load time and the size.
12. **What other programs show of the database's own custom data** to a user, which section 14
    assumes is nothing.
