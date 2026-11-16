# Ground report: what KeePassGate stores in a database, and what a new store has to meet

Key: `inventory`. Written 2026-11-15.

Sources, all read only:

- the product at `a42e38aa`, detached at master `a42e38aa` (KeePass 2.62 under `.keepass\2.62`). Code is
  cited as `file:line` under `src\KeePassGate\` unless another root is named.
- the branch `spikes/revocation-merge` at `11a9aa12`, cited as "spike" with the section of its
  `RESULTS.md` or the file under `spikes\revocation\output`.
- One probe of my own, `sizes.ps1`, run in Windows PowerShell 5.1 over KeePass 2.62's KeePassLib and
  the plugin's Release build that sits in the review4 worktree (dated 2026-11-02, older than
  `a42e38aa`; the stored formats it writes are the ones the code at `a42e38aa` documents). It made
  an in-memory database with invented content, created an authority, issued one invented client and
  printed the lengths. Script and output are in
  `spikes\storage\probes\ground-inventory\`
  (`sizes.ps1`, `sizes.txt`). No window, no file database, nothing else run.

**How each statement is marked.**

- **measured (here)**: run by me for this report.
- **measured (spike)**: recorded as run in the spike's `RESULTS.md` and output files.
- **measured (host test)**: asserted by a test in the review4 tree whose own remarks say it measures
  the behavior against a real KeePass. I read the test; I did not run it (no test tier was run).
- **read**: from source at a named commit or tag, with file and line.
- **assumed**: neither of the above. Every assumed statement is listed again in section 9.

---

## 0. What a designer most needs from this report

1. **Seven kinds of datum live in the file**: six key families in `PwDatabase.CustomData` and one
   key, `KPG`, in the custom data of each group and entry that carries a rule (section 1). Nothing
   else of the plugin's is in the `.kdbx`; invitations, held approvals, this-session refusals,
   owed answers, the responder and the audit are not, and must stay out (section 1.3).
2. **The datum that matters most for security is small and add-only**: a revocation is 76 + 85
   characters, a retirement 85 + 7. What makes the present store work is that a KeePass merge never
   removes a key and never reads a value (measured (spike) §1, 70 of 70 cells). What breaks it is
   that other programs do not keep that promise (spike §5, §6).
3. **Three of the seven are changed in place rather than only added**: the profile list, the
   standing answers to renewals, and every rule. Those are exactly where a stale copy can win, and
   two of them, the profile list and the rules, are where a lost or stale value **widens** access
   (section 3).
4. **A rule that disappears is read as silence and inherits** (`Acl\AclDocument.cs:110-120`, read),
   and the programs that delete rules move no modification time (measured (spike) §6). So any new
   store must make the loss of a rule visible or refusing, not only durable (requirement E6).
5. **Entries are not merged field by field by KeePass, KeePassXC or Keepass2Android, as far as
   anything measured so far shows.** Groups and entries go whole by their modification time, an
   entry's older version into its history (measured (host test) and measured (spike) §5, §6). An
   entry carrying two fields added in two copies has not been measured in any program (section 4).
   That is the question the timestamp-named-field idea turns on.
6. **History is not a store**: KeePass keeps 10 revisions or 6 MiB by default (measured (here) on
   2.62), the maintenance dialog deletes revisions by age, and a restored revision replaces the live
   entry, its rule with it (read, `DESIGN.md:972-973`). Groups keep no history at all.
7. **The API today reaches no custom data at all** (read: no use of `CustomData` under `Vault\` or in
   `Pipeline\VaultHandlers.cs`). A store in entries or fields would sit inside what the API serves,
   and would have to be made as unreachable as the recycle bin is (`DESIGN.md:2206-2209`), for reads
   and above all for writes, or a client could forge its own record or rule (requirement E8).
8. **`DESIGN.md` §131 already names this moment**: "If records turn out to be lost often enough to be
   a support burden, to Database Maintenance, a merge or a declined save, `CustomData` is the wrong
   store and that decision has to reopen" (`DESIGN.md:2256-2261`, read).

---

## 1. Inventory

Sizes are measured (here) unless marked. "Key" length is the name of the key; "value" is the stored
text.

### 1.1 The database's own custom data, `PwDatabase.CustomData`

All keys begin `KPG.`, `Authority\AuthorityKeys.cs:21` (read). The rule key on nodes is `KPG` with no
dot, so a prefix test on `KPG.` misses it on purpose (`AuthorityKeys.cs:14-20`, read).

| # | Key | Value | Size | Written by, and when | Read by, and how often | What a merge must do with it |
| --- | --- | --- | --- | --- | --- | --- |
| D1 | `KPG.Authority.<sha256 of the authority certificate>` (`AuthorityKeys.cs:41`) | one JSON object `{"v":"1","ca":…,"host":…,"sniKey":…}`: base64 PKCS#12 of the authority certificate and key, base64 PKCS#12 of the host certificate and key, base64 of the 32-byte gate key (`Authority\AuthorityDocument.cs:26-28`, `:130-149`, read) | key 78 chars; value **2903 chars** | `AuthorityStore.Write` (`Authority\AuthorityStore.cs:150-177`, read) from Create and Recreate on the KPG tab, buffered by `PendingDatabase` and applied at the settings dialog's OK. **Refused over a document already there; never rewritten.** Removed by the prune once its retirement exists (`AuthorityStore.cs:310-320`) | **Every connection, for every open database**: `OpenDatabaseTargets.TargetFor` reads the text of every authority and retirement key (`AuthorityStore.Stored`, `:125-134`) and compares it with its cache, parsing only when it changed (`Bridge\OpenDatabaseTargets.cs:26-29`, `:794-816`, read). Also the KPG tab, the authority chooser, `AdmissionStore.Revocation` (to name the issuer), owed answers | Union of documents. The same key holds the same value in every copy, so a merge never has to choose. Parts of two authorities must never be combined (that reads as damage and serves nothing, `AuthorityDocument.cs:12-21`). Presence alone never serves: retirements decide |
| D2 | `KPG.RetiredAuthority.<sha256>` (`AuthorityKeys.cs:54`) | the constant `retired` (`:57`) | key 85; value 7 | `AuthorityStore.Retire`, only when absent (`AuthorityStore.cs:458-465`), at Recreate, Delete and Keep-one; **never changed or removed**. Withdrawn from the tab's buffer when the authority was made and retired on the same dialog (`Ui\PendingChanges.cs:305-319`) | With D1, on every connection | Union, and **never removed** by any merge or save. A retirement lost lets a stale copy's authority, its records and certificates back |
| D3 | `KPG.Client.<sha256 of the client certificate>` (`AuthorityKeys.cs:112`) | line format `v`, `subject`, `client`, `admitted`, `profiles`, `certificate` (base64 DER) (`Authority\AdmissionRecord.cs:239-249`; `CA.md` §223) | key 75; value **819 chars** with two profiles (client certificate 487 bytes of DER). 2824 chars for a record naming 32 profiles of 62 characters (record text only) | `DatabaseWriter.WriteAdmission` at pairing and at renewal, on KeePass's thread, naming any new profile in the same write, saved and verified before the certificate is handed over (`Bridge\DatabaseWriter.cs:1815-1997`, read). **Emptied** at revocation (`Authority\AdmissionStore.cs:371`): from the clients page at the dialog's OK, or at once by `DELETE /gate/certificate` (`DatabaseWriter.cs:866-905`). Removed by the prunes (expired; came back beside a revocation; an emptied one with its revocation) and by `RemoveAll` at Recreate and Delete (`AdmissionStore.cs:283-302`) | **Every request**, which is every connection (`DESIGN.md:256-266`): `ClientCredentials.For` (`Pipeline\ClientCredentials.cs:110-140`), and again inside a write's turn on KeePass's thread, `ClientCredentials.Again` (`:192-253`). `AdmissionStore.Find` lists every record, revocation and standing-answer key and compares each in fixed time (`AdmissionStore.cs:64-105`). Also the clients page, the approval dialogs, the Profiles page's counts | Union by certificate, so two copies pairing different clients keep both. A record beside a revocation admits nobody. A record lost must leave the certificate inert. Content never changes after the write except to be emptied |
| D4 | `KPG.Revoked.<sha256>` (`AuthorityKeys.cs:158`) | `<notAfter, RFC 3339 Z> <sha256 of the issuing authority>`, or the date alone, or empty for a record that would not read (`AuthorityKeys.cs:181-189`) | key 76; value 85 (20 + 1 + 64, by construction) | `AdmissionStore.Remove`, **only when absent, never changed** (`AdmissionStore.cs:362-369`). Removed by the prune once its certificate has expired or its authority is retired or gone (`:407-454`, `:711-752`), never when empty; removed by `RemoveAll` | With D3, on every request; only **presence** decides (`AdmissionStore.cs:842-852`); the value is read only by the prune | Union; must survive every merge and save. A pruned one coming back is harmless (its certificate is refused on its dates or by the retirement) |
| D5 | `KPG.RenewalRefused.<sha256>` (`AuthorityKeys.cs:243`) | `forever` or a moment (`Authority\RenewalRefusal.cs:104-115`) | key 83; value 7 or 20 | The renewal dialog through `DatabaseWriter.RefuseRenewal`, saved at once (`DatabaseWriter.cs:919-965`); when KeePass is busy, held in memory as owed and written into **every** save of that database at `FileSavingPre` (`:327-373`, `:1668-1731`). **Changed in place** by a later answer. Removed by Allow renewal at the settings dialog's OK and by the prune once no record admits the certificate (`AdmissionStore.cs:524-552`) | Each renewal request, before any dialog; the clients page (name drawn red). An unreadable value counts as forever (`AdmissionStore.cs:190-212`) | The later answer should win. The losing direction refuses and never admits; a stale forever answers `409 forever` until lifted (`CA.md:215-229`). A lift is a removal and does not travel today |
| D6 | `KPG.Profiles` (`Acl\ProfileNames.cs:45`) | `{"profiles":[…]}`, sorted (`:144-172`); at most 32 names (`:53`), each ASCII letters, digits, `-` and `_`, up to 64 | key 12; value 38 chars for three names, **2094** for 32 names of 62 | The Profiles page, at the settings dialog's OK, replayed as the operator's difference by `ProfileNames.Merge` (`ProfileNames.cs:251-275`, `Ui\PendingChanges.cs:169-201`); a pairing that approves a name the list lacks, in the same write as the record (`DatabaseWriter.cs:1580-1638`). Never written over a list that will not read | **Every request**, twice: `KnownProfiles` and `ProfilesUnreadable` (`OpenDatabaseTargets.cs:1248-1304`), and again in a write's turn; the grant editor's pick list; the tabs | Today one value taken whole (`CA.md:147-155`). What it should do: keep a name either copy added and keep out a name either copy removed. **A removed name coming back is a widening** (`docs\access-control.md:60-64`, read) |

### 1.2 A node's custom data, `PwGroup.CustomData` and `PwEntry.CustomData`

| # | Key | Value | Size | Written by, and when | Read by, and how often | What a merge must do with it |
| --- | --- | --- | --- | --- | --- | --- |
| N1 | `KPG` (`Acl\AclDocument.cs:66`) | one flat JSON object: `"default"` holding `inherit` or `deny`, and one array of words per profile from `create`, `read`, `update`, `strip`, `delete`, `list`, `attachments`, `protected` (`AclDocument.cs:34-58`, `:248-292`). At most 31 profiles (`:80`), 32 items per array (`Transport\JsonBody.cs:58-61`) | a typical rule of two profiles **101 chars**; the largest the reader accepts, 31 profiles of 64 characters with all eight words, **4482** (reads, measured (here)) | **Only the grant editor**, into the group or entry dialog's own working copy `m_sdCustomData` (`Bridge\NodeDialog.cs:17-22`, `:419-454`; `KeePassContract.cs:184`), kept by KeePass at that dialog's OK, discarded by its Cancel. A node whose last rule goes has the key removed (`NodeDialog.cs:416-417`). **The plugin writes no rule of its own** (`CA.md:127-138`). Read only on a history revision and under Edit Entries (`NodeDialog.cs:212-273`). Never rewrites the same rules in other words (CLAUDE.md, step 13) | **Every request**: `AclResolver` walks from the node to the root for each profile; a listing walks every entry; each text is parsed once per request (`Acl\AclDocuments.cs:10-21`). The grant editor; the Profiles page counts the live tree's rules | Today: travels with its node and is taken whole with it by the node's modification time (`CA.md:127-131`; measured (host test) `MergeInRealDatabaseTests.cs:267-286` for the counterfactual). What it should do: a rule that narrows or seals is never lost without trace and never replaced by an older, wider one |

Two facts about N1 that a new store inherits whichever way it goes:

- **An entry's history holds the rule each revision had**, and restoring a revision copies it back
  over the live entry (`DESIGN.md:972-973`, read). Today that restore is harmless only for a rule
  naming a removed profile, because the list decides what is in force (`DESIGN.md:980-984`).
- **Absent and empty are the same: silence, which inherits** (`AclDocument.cs:110-120`, read). A
  value that is present and will not read refuses there and below (`DESIGN.md:915-918`).

### 1.3 What belongs to a database and is deliberately not in it

A new store must not move any of this into the file. All read.

| What | Where it lives | Why not in the file |
| --- | --- | --- |
| Invitations (pairing tokens) | `Pairing\InvitationBook.cs`, plugin memory | expire on TTL or exit; a token in the file would replicate and outlive itself (`PAIRING.md` §306, `CA.md:403-404`) |
| Approvals KeePass was too busy to write | `Pairing\ApprovalBook.cs`, memory | the operator's ruling of 2026-10-14 (CLAUDE.md) |
| Standing answers "in this session" | `Pairing\SessionRefusals.cs`, memory | an answer about one session written into the file would outlast it in every copy (`PAIRING.md:680-683`) |
| Owed standing answers | memory until a save lands (`DatabaseWriter.cs:1640-1731`) | they are D5 values waiting for a save |
| Responder certificate and a copy of the gate key while locked | memory (`CA.md` §208) | a locked database has nothing else to answer with |
| Caches: authority material keyed by the stored text; digests a tag keeps | memory (`OpenDatabaseTargets.cs:26-29`; `DESIGN.md:832-862`) | derived |
| Audit, JSON Lines; the plugin's log | files outside the database (`DESIGN.md` §120) | |
| `KPG.Port` | KeePass's own configuration (`Bridge\KeePassSettings.cs:13-20`, `:42`) | one answer per installation, not per database |

**Derived from D1, so a change in how D1 is stored moves them**: the database's identifier (the
authority's digest); the SNI names, from the gate key (`DESIGN.md` §112); and the key a node's tag
is made with, derived from the host PKCS#12 "as the database stores it", so every tag would change
once, which `API.md:253-259` says is no part of the contract (read).

### 1.4 Volume

From the sizes above, a database with one authority, two retirements, ten clients, three
revocations, a list of five names and rules on twenty nodes holds about 2.9 + 0.2 + 8.2 + 0.5 + 0.1 +
2.0 KB, some 14 KB of KPG data (arithmetic on measured sizes). Records grow with clients and are
pruned as their certificates expire (`CA.md:201-208`); retirements grow by one per authority ever
retired and never shrink (spike §3).

---

## 2. Properties the present design leans on

Each is something the code or the documents rely on being true. A new store keeps it, replaces it
with something that gives the same guarantee, or records that it is given up.

| # | Property | Basis | Where recorded |
| --- | --- | --- | --- |
| P1 | A KeePass merge of `PwDatabase.CustomData` visits the source's keys only, never removes a target key and never reads a value; a key both hold is decided by the two times, the method and `SettingsChanged` | measured (spike) §1, `output/rules.txt`; read, KeePass 2.61.1 `PwDatabase.cs:1541-1632` as the spike cites it; pinned by `CustomDataMergeRuleTests` (host test) | CLAUDE.md, measured facts; `CA.md:157-167` |
| P2 | A key added once and never changed survives every KeePass 2.61.1 merge and import method, whatever was stripped and whatever the clocks | measured (spike) §2, 70 of 70 cells | `CA.md:157-167` |
| P3 | One key per authority, written once, plus a retirement per retired one; which is served is decided by retirements and never by counting documents | measured (spike) §3, no `WRONG` cell, 48 + 192 three-copy runs | `AuthorityStore.cs:19-25`; `CA.md:238-258` |
| P4 | One key per certificate, so two copies that paired different clients keep both | measured (host test) `MergeInRealDatabaseTests.cs:67` | `CA.md:140-145` |
| P5 | A group's custom data has no per-key time and is replaced whole from whichever copy's group was touched last | measured (host test) `MergeInRealDatabaseTests.cs:267-286` | `Bridge\CustomDataStore.cs:13-20` |
| P6 | The admission record is read on every request and never cached; no record, an unreadable one, one beside a revocation, or an expired certificate, admits nobody; the profiles are the record's less every name the list lacks | read `ClientCredentials.cs:28-40`, `:110-140`, `:255-270`; `AdmissionStore.cs:49-105` | `CA.md` §206, §217 |
| P7 | That rule turns a record lost to a stale dialog copy, a failed save or "Discard changes" into a client that pairs again, rather than a certificate nobody can see or revoke | read | `CA.md:426-432`; `DESIGN.md:650-658` |
| P8 | Pruning rides only on the plugin's own writes; nothing depends on it having run; **opening or reading a database changes nothing in it** ("operator answer 3a") | read `DatabaseWriter.cs:1795-1799`; `AuthorityStore.cs:288-294` | `CA.md:182-186` |
| P9 | The KPG settings tab buffers until the dialog's OK and replays key by key, with reconcilers for the keys that have second writers: `KPG.Profiles` as a difference, `KPG.RenewalRefused.*` keeping the later answer, `KPG.Revoked.*` keeping the one there | read `Ui\PendingChanges.cs:38-125`, `:405-867` | CLAUDE.md, "Everything on the KPG settings tab waits for that dialog's OK" |
| P10 | The grant editor writes into the dialog's own copy `m_sdCustomData`, which exists only once the dialog has loaded | read `NodeDialog.cs:17-22`, `:76-102`; measured (host test) per `FINDINGS.md:261-262` | CLAUDE.md, "The grant editor writes into the dialog's own copy" |
| P11 | KeePass's group and entry dialogs put their copy of a node's custom data back on OK; Database Maintenance puts its copy of the database's custom data back whenever the key count differs, whatever was pressed; Database Settings puts its recycle bin choice back. The writer refuses what each would undo | read `GroupDialogs.cs:422-432` and `DESIGN.md:2039-2061`; measured (spike) §4 for Maintenance, where a recreate under the open dialog was undone and **the old authority served** | CLAUDE.md, "The trap that shaped the design" |
| P12 | The file is KDBX 4.1 because a per-key time is a 4.1 field; KeePass writes 4.1 for custom data only when some key carries a time | read KeePass 2.61.1 `KdbxFile.cs:390-394` as the spike cites it; measured (spike) §5, §6 | CLAUDE.md, "The authority takes the file to KDBX 4.1" |
| P13 | Every write is saved on KeePass's thread and believed only when `Modified` is false and the file hashes to `HashOfLastIO`; a write that did not land is taken back out with its prunes | read `DatabaseWriter.cs:14-71`, `:1951-1997` | CLAUDE.md, the trap section; `DESIGN.md` §119 |
| P14 | At lock the plugin overwrites its own arrays and lets the keys go; the base64 text KeePass keeps in custom data is an immutable plain string nobody can overwrite, which is conceded | read | `CA.md:499-518` |
| P15 | **What the API hides**: no API path reaches the authority key; no route reads or writes any custom data; the recycle bin is not listed, readable, writable or addressable; a refused node and an absent one answer the same 404 | read `DESIGN.md:640-643`, `:2206-2209`; grep of `src\KeePassGate\Vault` and `Pipeline\VaultHandlers.cs` finds no `CustomData` | `DESIGN.md` §111, §138; `API.md` §401 |
| P16 | A removal through the API stamps what KeePass's own delete stamps and no node or parent modification time, so it never outranks a narrowing made in another copy | measured (host test) `RemovalMergeTests.cs:93-113` | `DESIGN.md:2216-2223` |
| P17 | An identical value is never written again, because `StringDictionaryEx.Set` stamps a fresh time even for the same text | read `CustomDataStore.cs:58-75`, `PendingChanges.cs:818-827`; KeePass `StringDictionaryEx.cs:152-160` as the spike cites it | `CA.md:137-138` |
| P18 | Owed standing answers ride every save of their database at `FileSavingPre`, except a copy and except while Database Maintenance is open | read `DatabaseWriter.cs:327-373` | `PAIRING.md:684-696` |
| P19 | More than one unretired authority serves nothing (`503 undecided`); the operator is asked over the main window at open, unlock, or the save that ends a synchronize, never from a connection. **KeePass announces a synchronize to a plugin only by that save** | read `CA.md:249-287`; `Bridge\MainWindowDatabases.cs:60-62` | CLAUDE.md, decided |
| P20 | A revocation is the presence of D4; the record is emptied as well | read `AdmissionStore.cs:304-387` | `CA.md:157-180` |
| P21 | Recreate and Delete remove every record and revocation and rely on the retirement to refuse anything a stale copy brings back | read `AdmissionStore.cs:266-302`; `AuthorityStore.cs:179-228` | `CA.md:289-302` |
| P22 | A record carries the whole certificate (expiry known, prune possible, naming) and a client key written once | read `AdmissionRecord.cs:16-30`, `:110-129` | `CA.md:834-848` |
| P23 | Whoever can write the file can write a whole record, so nothing in a record is authenticated beyond the file | read `AdmissionRecord.cs:98-125` | `CA.md:62-64`, `:846-848` |
| P24 | Stored formats are a flat JSON object of strings and arrays read by `JsonBody`, and the record's line format read by its own strict reader; reaching a ceiling denies rather than truncates | read `AclDocument.cs:28-58`; `AdmissionRecord.cs:226-249` | CLAUDE.md, JSON grammar; `CA.md:870-887` |
| P25 | Every read holds KeePass's thread still, and no read starts inside a merge (detected from the thread's own stack) | read `OpenDatabaseTargets.cs:20-24`; `Bridge\MergeWatch.cs:8-34` | `docs\architecture.md:142-145` |
| P26 | KeePass's XML export writes the database's custom data in the clear, authority key included, and tells no plugin | read | `CA.md:375-380`, `:938-939` |
| P27 | Two copies of one database open at once share an authority and are refused | read | `DESIGN.md:698-700`; `CA.md:936-937` |

---

## 3. What other programs were measured to do to each datum

All cells measured (spike), `RESULTS.md` §2, §5 and §6 with the outputs named there, unless marked.
"Kept" means the key and value came back; "times" means the per-key modification times. KeePassXC
covers 2.4.1, 2.4.2, 2.6.6, 2.7.0, 2.7.12 and 2.8.0; Keepass2Android 1.15-r6 and 1.08d-r5; KeePassDX
4.5.8, 3.2.0 and 3.3.0 (save only); KeePassDroid 2.7.

| Datum | KeePass synchronize and import | KeePassXC save, KeePass 2.47 save, Keepass2Android save | KeePassXC merge | Keepass2Android merge | KeePassDX | KeePassDroid save |
| --- | --- | --- | --- | --- | --- | --- |
| D1, D2, D4 (keys only ever added) | kept in every cell | kept, times gone (KeePassXC from 2.7.0 keeps them when another 4.1 feature is in the file) | **from 2.4.2, the copy loaded a second or more later replaces the whole dictionary: added keys removed** (96 of 96 slow runs); 2.4.1 merges no database custom data | keys only one copy holds are in the result, all 18 runs | kept with times (save); merge by their own times, 18 of 18 | kept with times |
| D3 record | union; a stale copy can bring an emptied record back, refused by D4 | kept, times gone | set back to the other copy's | a key both hold keeps the open copy's value unless the arriving copy's `SettingsChanged` is later; a stale record came back in 9 of 18, refused by D4 | by times | kept |
| D5 standing answer | by time, or the source's under Overwrite existing (`CA.md:215-229`, read) | not in the spike's sets (assumed: as D3) | assumed: as D3 | assumed: as D3 | assumed: by times | assumed: kept |
| D6 profile list | one copy's whole list (`CA.md:147-155`, read) | kept, times gone | set back to the other copy's | **a stale, wider list came back** with the stale copy open, or arriving with later settings | by times | kept |
| N1 rule on a group | the newer group whole | kept; KeePassXC adds `_LAST_MODIFIED` to the node's custom data without moving its time | **the target's rule kept, the newer time taken**, so a narrowing made in the other copy is lost without looking older | P's rule and P's time both ways | 4.5.8 merge: **the arriving copy's rule replaces the open copy's whichever is newer**, under the newer time; 3.2.0 and 3.3.0 save: **every group's rule deleted, time unchanged**; 4.5.8 gave the root group the time of the save | **every rule deleted, times unchanged** |
| N1 rule on an entry | the newer entry whole, older into history (section 4) | kept | **the newer entry whole, the older in its history**, 216 runs | P's rule, one older version in history | 4.5.8 merge: arriving copy's rule put; 3.2.0 and 3.3.0 save kept entry rules | **deleted, time unchanged** |

Read from source in the spike and repeated here because it bears on any store: Keepass2Android loses
every per-key time **at load**, not at save (spike §6, `KdbxFile.Read.Streamed.cs:639-640` at
`v1.15-r6`); KeePassDroid's writer writes custom data for the database and nothing else
(`PwDbV4Output.java:360` at `v2.7`); KeePassDX's `mergeCustomData` puts an arriving item whenever
either item has no time and removes nothing (`DatabaseKDBXMerger.kt:415-432` at `4.5.8`); a
KeePassXC merge stamps every key it copies with the merge's time, so stale values come out as the
newest in the file (`Merger.cpp:758`, `CustomData.cpp:88-89` at `2.7.12`).

**What this means per requirement.** D1, D2, D4 fail only under a KeePassXC merge. D3 fails
closed everywhere it was measured. D6 and N1 widen access under several programs. That is the
present store's whole scorecard, and it is what section 5 is written against.

---

## 4. The question the timestamp-named-field option turns on: one entry changed in both copies

Every ground report answers this for its own subject. Mine is the inventory and the measurements
already made, so this is what is already known, and what is not.

### 4.1 Same entry in both copies, each copy adding or changing a different field

| Program | What the merge leaves | Basis |
| --- | --- | --- |
| KeePass 2.61.1 synchronize | **The newer entry, by modification time, whole**: its strings are what the entry holds afterwards, and the other copy's edit is in the entry's history. A test that asserts the fix records the behavior it fixed: "the binned copy was the newer content, and the other copy's edit, made before the removal, went into the entry's history" | measured (host test) `tests\KeePassGate.Host.Tests\RemovalMergeTests.cs:115-133` |
| KeePass, group | the newer group whole, custom data included; two keys added to one group's dictionary in two copies do **not** both survive, "it arrives by replacing the dictionary, taking the other write with it" | measured (host test) `MergeInRealDatabaseTests.cs:266-286` |
| KeePass, entry with a **different field added on each side** | not measured anywhere. By the whole-entry rule above, the live entry would hold the newer copy's fields only and the older copy's added field would survive only in history | assumed, from the two rows above |
| KeePassXC 2.4.1 to 2.8.0 | "An entry takes the newer entry whole", with the older in its history, 216 of 216 runs. The two copies differed in a rule only, the newer in every way, so this is not the different-fields case | measured (spike) §5 |
| Keepass2Android 1.15-r6, 1.08d-r5 | P's rule and P's time in all 18 runs either way, one older version in history; same caveat; its merge is a port of KeePassLib's `Synchronize` (read, spike §6, `SaveDB.cs:338`) | measured (spike) §6 |
| KeePassDX 4.5.8, 3.2.0 | entry custom data: arriving copy's item put even when the open entry is newer. **Entry strings: not measured and not read** | measured (spike) §6 for custom data |
| KeePassDroid 2.7 | has no merge | measured (spike) §6, screens; read, `strings.xml` at `v2.7` |

### 4.2 What is in the history, and for how long

- **What goes in.** In KeePass the older version is kept by a backup of the entry, and histories are
  matched across a merge by modification time, the local revision kept where both carry the same
  time (`DESIGN.md:973-975`, read). KeePassXC and Keepass2Android kept the older version (measured
  (spike), above).
- **How long.** `CreateBackup` applies `HistoryMaxItems` and `HistoryMaxSize` itself (read off
  KeePass 2.61.1, `FINDINGS.md:356-360`). The defaults on KeePass 2.62 are **10 revisions and
  6291456 bytes**, and a new database's `MaintenanceHistoryDays` is **365** (measured (here), by
  reflection). That the Database Maintenance dialog uses that value to delete older revisions is
  assumed. The API's own updates add revisions the same way (`Bridge\KdbxVault.cs:1257-1287`, read),
  so a busy entry rotates its history.
- **Groups keep none** (`RemovalMergeTests.cs:99-102`, read), so for a group there is nowhere a lost
  version could even be found.
- **A restored revision replaces the live entry**, rules included (`DESIGN.md:972-973`, read).
- **Other programs pruning or dropping history on save**: not measured; KeePassDroid's handling of
  history is unknown.

### 4.3 Equal times, and times dropped or rewritten

- **Equal times, KeePass**: a synchronize whose two entries carry the same modification time
  **takes the copy merged in** and leaves the time as it was (measured (host test) against 2.61.1,
  `NodeTagsTests.cs:927-948`, `:968-981`). So at a tie the result depends on which copy is open.
  This is the opposite of the custom-data rule, where a tie keeps the target (spike §1).
- **Whole seconds**: a KDBX 4 file keeps a node's time to the second (measured, `FINDINGS.md:414`),
  so two edits in one second in two copies are a tie.
- **A move changes `LocationChanged` and not the modification time** (measured, `FINDINGS.md:417`),
  and a merge relocates by `LocationChanged` separately from content
  (`RemovalMergeTests.cs:130-132`, measured (host test)).
- **Rewritten or untouched times elsewhere**: KeePassDroid and KeePassDX 3.2/3.3 delete rules
  without moving any time; KeePassDX 4.5.8 stamps the root group at every save; a KeePassDX merge
  leaves an older rule under the newer time (measured (spike) §6). A store that decides by node
  times inherits all of these.
- Equal times and dropped times have not been measured for entry strings in any program but
  KeePass (to measure).

### 4.4 Two different entries added to the same group, one in each copy

| Program | Result | Basis |
| --- | --- | --- |
| KeePassXC | an entry only the source held (`xc-touch`, added by a KeePassXC save of C) arrived in the target, and an entry only the target held stayed, in every `saved` run | measured (spike), `output/keepassxc-merge.txt`, e.g. rows 8-13 |
| Keepass2Android | an entry added in the open copy only (`droid-merge`) was in every result | measured (spike), `output/android-merge.txt:25-36` |
| KeePass | not measured in either tree; assumed both arrive, since a merge matches entries by identifier | assumed |
| Any program, an entry added on **both** sides at once | not run | to measure |

**Deletions travel for entries, unlike custom data keys.** CLAUDE.md names KeePass's
`ApplyDeletions` as part of the merge (read), so an entry deleted in one copy can be deleted in the
merge result; how each program carries deleted objects was not measured (to measure). For a store
built of entries that are only ever added, this is the analogue of "a merge never removes a key",
and it does not hold in the same way.

### 4.5 What that means for a JSON-in-a-timestamp-named-field store

Derived from the facts above, so assumed until section 9's measurements are made:

- **One entry holding fields named by time does not keep both copies' additions in the live entry**
  under KeePass, KeePassXC or Keepass2Android: the newer entry's field set wins, and the other's
  addition is in history, which holds 10 revisions by default and none for a group. A design that
  keeps one datum per **entry** rather than per field would rest on 4.4 (entries only one copy holds
  are kept), which is measured only one-sided so far.
- **Reading history to recover lost fields** would make durability depend on `HistoryMaxItems`,
  `HistoryMaxSize`, the maintenance dialog and every other program's history handling.
- **A "preprocessing step when loading"** that writes would contradict P8 (opening changes nothing,
  operator answer 3a). The existing hooks are the plugin's own writes and `FileSavingPre` (P18), and
  the only notice of a synchronize is the save that ends it (P19).

---

## 5. Requirements any new store has to meet

Three groups. **E** is essential to security: fail closed, a lost or stale datum never widens
access, a revocation survives. **A** is availability and correctness the operator relies on, where a
failure is closed but costly. **H** is a habit of the present store: something it does that a new
store may drop, as long as the E and A requirements it served are met another way. Operator rulings
the store touches are listed as **O**, because they are neither security nor habit and only the
operator can reopen them.

"Today" says whether the present store meets it.

### 5.1 Essential (security)

| # | Requirement | Reason | Recorded | Today |
| --- | --- | --- | --- | --- |
| E1 | **Fail closed.** A datum that is missing, unreadable, of an unknown version or damaged, or a read that fails, admits nobody and grants nothing; a failed listing is "busy", never "none" | the plugin guards every secret the operator has; the caller is in the threat model | CLAUDE.md "Security posture"; `CA.md:382-388`; `AdmissionStore.cs:24-31`; `AclDocument.cs:47-49`; `ProfileNames.cs:38-40` | met for what the plugin can see; see E6 |
| E2 | **A certificate admits only through a record in the database**, its profiles read from that record (less names the list lacks) and never from the certificate, and a lost record leaves the certificate inert | a record lost to a stale copy must not leave a credential nobody can see or revoke | CLAUDE.md "The admission record is load bearing"; `CA.md` §206, §212 | met |
| E3 | **A revocation survives every save and every merge, by KeePass and by the other programs**, in both directions, whatever the clocks, the times and the formats | a revoked client admitted again is the worst outcome the store can produce | `CA.md:157-180`; the survival requirement in the task | met for KeePass merges, every save measured and the Android merges; **fails under a KeePassXC merge from 2.4.2** (spike §5) |
| E4 | **A retirement survives the same way**, a retired authority never serves again, and the decision never rests on counting documents or on a prune having run | Recreate and Delete remove every record and revocation and rely on the retirement alone (P21); a lost retirement lets a stale copy's authority and clients back | `CA.md:249-258`, `:289-302` | as E3; fails under a KeePassXC merge |
| E5 | **A narrowing is never undone by another program's save or merge**: a rule narrowed, emptied or sealed is never lost, never replaced by an older wider one; a profile taken off the list never comes back; a record's profiles never widen | the threat model includes a legitimate client asking for more; a deleted rule "widens what a client reaches where it narrowed an inherited grant" | spike §6 "What this leaves the plugin"; `docs\access-control.md:60-64`; `CA.md:315-320` | **fails**: rules deleted by KeePassDroid and KeePassDX 3.2/3.3; stale rule under a newer time by a KeePassDX merge; a group's rule kept from the target by a KeePassXC merge; the list taken whole by KeePass and Keepass2Android |
| E6 | **The loss of a rule is detectable, or refuses.** An absent rule must not be indistinguishable from a rule another program deleted, since absence inherits and the deleting programs move no time | without it E5 cannot be checked, only hoped for | derived: `AclDocument.cs:110-120`; spike §6 "nothing about G shows that the rule went" | **fails** |
| E7 | **No credential before its record is in the file**: written, saved, `Modified` false and the file hashed back, before the certificate is handed over; a write that did not land is taken back out | a certificate with no durable record is a credential nobody can revoke | `PAIRING.md` §303 phase 5; `DatabaseWriter.cs:14-71`, `:1972-1991` | met |
| E8 | **The API reaches none of it, and writes none of it.** No route reads, lists, counts, tags or addresses the authority, records, revocations, retirements, answers, list or rules, and no route can create, change, move or remove the place they are kept | a client able to write where records or rules live could admit itself or widen its own rule; a client able to read the authority could mint identities | `DESIGN.md:640-643`; `CA.md:54-57`; `DESIGN.md:2206-2209` for the bin as precedent | met structurally, since no route touches custom data (P15). A store in entries, fields or groups would need it added explicitly, including for an agent holding `create` or `update` where the store sits |
| E9 | **The trust domain stays the database.** Everything lives inside the encrypted `.kdbx`; no host registry, no sidecar; a database's data admits only to itself; two open copies of one database are refused | a registry that names databases is attacker-suppliable, because a database is a file | CLAUDE.md first decided bullet; `CA.md` §202; `DESIGN.md:681-700` | met |
| E10 | **Secrets are protected at rest by the database's encryption only**, and never put anywhere else the plugin controls (no key file in the user's profile, no passphrase scheme) | delegating to KeePass's encryption beats cryptography written here; keys must stay off the disk | CLAUDE.md; `CA.md:357-373` | met |
| E11 | **A KeePass dialog's stale write-back never silently undoes a revocation, a retirement or a narrowing**: a write it would undo is refused while it is open, or its loss fails closed | measured: a recreate under Database Maintenance was undone and the old authority served | spike §4; CLAUDE.md trap; `GroupDialogs.cs:422-432` | met by refusing |
| E12 | **No silent choice between authorities**: several unretired authorities serve nothing until the operator keeps one | a merge must not pick an authority for the operator | `CA.md:249-287` | met |
| E13 | **The plugin's own writes move no node time a KeePass edit would not**, so they never outrank a narrowing made in another copy | a merge takes a group whole by its time | `DESIGN.md:2216-2223`; `RemovalMergeTests.cs:93-113` | met |
| E14 | **Live reads**: a revocation, a profile removal or a rule change bites from the next request; nothing that grants is cached across requests | revocation is meant to be immediate and offline | `CA.md:793-797`; `DESIGN.md:265-266`, `:939-942` | met |

### 5.2 Availability and correctness

| # | Requirement | Reason | Recorded | Today |
| --- | --- | --- | --- | --- |
| A1 | Pairings made on two copies both survive a merge | otherwise a client must pair again | `CA.md:140-145` | KeePass and the Android merges yes; KeePassXC merge no |
| A2 | An authority's three parts are indivisible and identical in every copy; a mixed or damaged one serves nothing | a mixed one reads as damage, and the only remedy revokes every client | `AuthorityDocument.cs:12-21` | met everywhere measured |
| A3 | A profile added in either copy survives a merge | a lost name grants nothing until added again, and the clients page shows it removed | `CA.md:147-155` | fails (one value taken whole) |
| A4 | The later standing answer to a renewal wins; the losing direction refuses and never admits | a stale forever costs `409 forever` until lifted | `CA.md:215-229` | by time; turned around by dropped times |
| A5 | Bounded growth: expired records and revocations go; only retirements accumulate | a client renewed weekly otherwise makes keys without bound | `CA.md:188-208` | met |
| A6 | Cheap reads: the record, the list and every rule on the walk are read on every request on a connection thread holding KeePass's thread still; the authority parsed only when its stored text changes | the gate shares KeePass's thread | `OpenDatabaseTargets.cs:20-29`; `FINDINGS.md` §20-22 | met |
| A7 | Other programs' additions are tolerated and never read as the plugin's data (`_LAST_MODIFIED`, `KPXC_RANDOM_SLUG`, `KPXC_autosaveDelayMin`, `KPXC_REMOTE_SYNC_SETTINGS`) | measured additions in KeePassXC saves, including in node custom data | spike §5 | met (`KPG` names) |
| A8 | Damage is shown and named on the KPG tab rather than repaired silently, and the operator can take it away there; nothing depends on hand edits elsewhere | an operator must be able to see what a merge did | `CA.md:321-332`; `docs\clients.md:59-71`; the 2026-11-08 ruling on a damaged list (`PendingChanges.cs:162-167`) | met |
| A9 | The store survives ordinary use of KeePass and of the other programs: viewing, searching, editing other entries, Database Maintenance's commands, export and import | the survival requirement: other programs must leave the data intact | the task; `CA.md:304-322` | Maintenance's Plugin Data tab can delete keys by hand (`CA.md:324-332`) |
| A10 | A KeePass merge of a file another program saved still meets E3 and E4 (the "strip" condition) | the file passes through other programs between KeePass merges | spike §2 | met by add-only keys |

### 5.3 Operator rulings a new store touches

| # | Ruling | Recorded |
| --- | --- | --- |
| O1 | Opening or reading a database changes nothing in it; prunes ride only on the plugin's own writes ("operator answer 3a") | `DatabaseWriter.cs:1795-1799`; `CA.md:182-186` |
| O2 | Everything on the KPG settings tab waits for that dialog's OK; Cancel means Cancel; revocation waits too, as a cost argued and accepted | CLAUDE.md; `PendingChanges.cs:38-44` |
| O3 | On the group and entry dialogs, OK keeps a rule and Cancel discards it | `NodeDialog.cs:17-22` |
| O4 | Owed standing answers are written by every save of their database, 2026-11-04 | `DatabaseWriter.cs:332-336` |
| O5 | A profile list that will not read is left alone and replaced only on the operator's press, 2026-11-08 | `PendingChanges.cs:162-167`; `DESIGN.md:948-951` |
| O6 | Clients are counted and named by client key, 2026-10-16 | `CA.md:837-854` |
| O7 | An older stored shape is not read or migrated (precedent: the single `KPG.Authority` key reads as no authority) | `CA.md:245-247`. That 0.1.0 is released and 0.2.0 is not (task) makes whether a database made with 0.1.0 needs carrying over a question for the project |

### 5.4 Habits of the present store

| # | Habit | Which requirement it serves | Note |
| --- | --- | --- | --- |
| H1 | KDBX 4.1 and per-key modification times | A4, D6 merges | **ruled out** by the survival requirement: other programs drop the times (spike §5, §6) |
| H2 | Everything in `PwDatabase.CustomData` under `KPG.`, rules under node key `KPG` | E9 | the location under review |
| H3 | Presence of keys only ever added, never values | E3, E4 | holds for KeePass and the Android programs; not for a KeePassXC merge |
| H4 | One key per authority, per certificate, per revocation, per answer | A1, A2, E3 | |
| H5 | The rule stored on the node it governs, travelling with it through moves, deletions and merges | E5, O3 | is also why foreign programs reach it |
| H6 | Values held as plain strings in KeePass's memory | (none; a concession, P14) | a protected string would be kept encrypted in KeePass's memory, and the plugin already reads protected values without decoding them into KeePass's object (`FINDINGS.md:710-738`); KeePass's own save reads as text only the five standard fields the database leaves unprotected (`FINDINGS.md:731-738`) |
| H7 | Formats: a flat JSON object through `JsonBody`, and the record's line format | E1 | CLAUDE.md: one grammar, one escaper |
| H8 | A revocation's value carries `notAfter` and the issuing authority, for the prune | A5 | |
| H9 | Database Maintenance is the one dialog that copies the store | E11 | a store in entries meets the entry dialog and Edit Entries instead (P11) |
| H10 | The record holds the whole certificate | A5, A8 | |

---

## 6. Settled decisions in CLAUDE.md a move of the store would touch

From "Things that are already decided", the measured facts, the trap section and "Where to start".
Each would need its reversal recorded, which CLAUDE.md asks for ("reversals are recorded rather than
edited away").

| Decision in CLAUDE.md | What a move does to it |
| --- | --- |
| "Every database is its own certificate authority, stored inside itself under `KPG` keys in the database's own `CustomData`" | the location changes; the principle stays (E9), and a host-level authority or registry stays forbidden |
| "The database's own `CustomData`, never the root group's", with `AuthorityDocument` as one key, keys only ever added, `KPG.Revoked.<key>`, `KPG.Authority.<fp>`, `KPG.RetiredAuthority.<fp>`, `503 undecided`, and "This reverses two rules" | the whole bullet is about this store and would be rewritten; its two reversals are the record of why |
| "The authority takes the file to KDBX 4.1" | goes if nothing depends on per-key times any more |
| "The admission record is load bearing" | stays (E2); the `GroupForm` trap it cites applies to a store in groups or entries again |
| "The authority key has no passphrase" | stays (E10); a protected string would add KeePass's memory protection (H6) |
| "Pairing tokens live in plugin memory only", with `ApprovalBook` "compared under the writer's lock at the write with the authority, the profile list and the renewed record" | the comparisons move to the new store; nothing in memory moves into the file (section 1.3) |
| "Two host certificates per database", and `CA.md` §208 on erasure | touched by how KeePass holds the stored keys |
| "Handlers catch what they named" (`Expected.OfCustomData`) | the filters follow the new store's failure modes |
| "Everything on the KPG settings tab waits for that dialog's OK", "Replayed key by key", "`KPG.Profiles` is replayed as a difference", "A standing answer to a renewal is the other key with a second writer", "Revocation waits too" | rebuilt on the new store's unit of change |
| "The grant editor writes into the dialog's own copy, `m_sdCustomData`" | goes if rules leave node custom data; O3 then needs another mechanism |
| "Spec-first routing" and "The API is generated by two rules", with refused and absent answering the same 404 | any place the store occupies inside the tree must be absent to every route (E8), as the recycle bin is |
| "The JSON grammar is `Newtonsoft.Json`'s and the policy is ours"; "nothing here escapes a JSON string" | any new stored JSON goes through the same writer and reader (H7) |
| "The access control model is one document per node … Grants live under a `KPG` key", "Nearest wins, per profile", "An empty array is a rule", "A document that will not read refuses", "There is no per-subject opt-in" | location of the document changes; E6 adds a meaning for an absent document that today is silence |
| "PyKeePassGate is the reference client" | untouched only if the API's visible surface does not change (E8) |
| Measured fact: "A KeePass merge of `PwDatabase.CustomData` visits the source's keys and no others, and never reads a value" | stays true and stops being load bearing |
| Measured fact: "`MainForm.SaveDatabase(db, null)` saves a named database", the busy signals and `GroupDialogs.Working` | the single writer and its guards stay; the dialogs that guard a store in entries are the entry dialog and Edit Entries |
| "The trap that shaped the design": `GroupForm`, `DatabaseOperationsForm`, the adding dialogs, "KeePass's save reports nothing", "`Modified` alone turned out to be too generous" | the save rules stay (E7); which dialogs copy the store changes |
| "The storage decision survived the thing most likely to invalidate it" and `DESIGN.md` §131's falsifier | this is that falsifier firing, for other programs rather than KeePass's own dialogs |
| "The profile list is the database's own names" and "the list decides what a client holds" | stays as a rule; the list's merge has to change (E5, A3) |
| "What step 13 settled about the editor": never writes the same rules in other words, since the entry dialog turns a respelled rule into a revision | holds for any store an entry dialog compares |
| "Two things about records": read live on every connection, and "a record written into the `.kdbx` file underneath a running KeePass is invisible to it" | stays (E14); every write goes through the plugin in memory |

---

## 7. Hazards a store in entries or groups meets that the present store does not

So the designer can weigh them against section 3. Basis as marked; most are assumed and listed in
section 9.

1. **The API serves entries and fields.** `createField`, `readField`, `updateField`, `stripField`,
   `listFields` and the listings (`Spec\openapi.yaml:963-1572`, read) would reach a store entry
   unless every route excludes it, the flat sweep from the root included. A meta entry inside a
   governed group is inside that group's rules (E8).
2. **A group is removed only once it is empty** (`DESIGN.md:780-783`, read); a meta entry inside
   every ruled group makes it never empty to KeePass, and visible in every program's view.
3. **Entry dialogs put their copy back on OK** (P11), so a plugin write to a store entry the operator
   has open is lost or must be refused, and Edit Entries refuses writes to every entry of that
   database (`DESIGN.md:2045-2048`, read).
4. **History**: every API update and every dialog edit adds a revision (section 4.2), so a store
   entry's past values, revoked records included, would sit in its history until rotated out, and a
   restore would bring them back (assumed consequence).
5. **Deletions travel** for entries (section 4.4), so an entry the operator deletes by hand, or a
   program's cleanup deletes, is deleted in every copy at the next synchronize, unlike a custom data
   key (assumed until measured).
6. **KeePass's own maintenance** (delete duplicate entries is one of the operations CLAUDE.md lists
   under `UIBlockInteraction`; delete history by age) acts on entries (read for the list; effect on a
   store entry assumed).
7. **Export and print**: KeePass's exports write entries' strings; custom data already goes out in
   the XML export (P26). The reach of other formats is assumed.
8. **A rule kept away from its node** (a global meta entry keyed by group identifier) no longer
   travels with the node through a move, a deletion or a merge in another program (assumed).
9. **KeePassDX 4.5.8 stamps the root group at every save** (measured (spike) §6), so anything that
   decides by the root group's time, a root rule included, goes to whichever copy KeePassDX saved
   last at the next KeePass synchronize (assumed consequence).
10. **What survived where custom data did not**: KeePassDroid 2.7 and KeePassDX 3.2.0 and 3.3.0
    kept every entry they were given, in their places, in every file they saved (measured (spike),
    the `entries:` column of `output/android-save.txt:386-532` and `:557-654`), while deleting the
    rules. Whether they keep **custom string fields** and their protected flag on save was not
    checked (to measure).

---

## 8. Facts from my own probe

`sizes.txt`, measured (here) on KeePass 2.62.0.0 with the 2026-11-02 Release build:

| What | Length |
| --- | --- |
| `KPG.Authority.<fp>` value, one authority | 2903 chars |
| client certificate with two profile claims | 487 bytes of DER |
| admission record, two profiles | 819 chars |
| admission record text naming 32 profiles of 62 characters | 2824 chars |
| profile list, three names | 38 chars |
| profile list, 32 names of 62 characters | 2094 chars |
| a typical two-profile rule | 101 chars, reads |
| the largest rule the reader accepts (31 profiles of 64 characters, all eight words, `deny`) | 4482 chars, reads |
| `PwDatabase.DefaultHistoryMaxItems`, `DefaultHistoryMaxSize` | 10, 6291456 |
| `MaintenanceHistoryDays` of a new database | 365 |

The first line of `sizes.txt` printing empty history constants was my script reading them wrongly;
the reflection line appended below it is the value.

---

## 9. Still to measure

Each is something this report had to assume or could only report as unmeasured.

1. **One entry, two copies, a different custom string added in each** (and a different field changed
   in each): what the live entry holds and what its history holds, under KeePass 2.62 for every
   merge method, `keepassxc-cli merge` in the six pinned releases, Keepass2Android's merge and both
   KeePassDX merge paths. Repeat at equal modification times and after a save that dropped or
   rewrote times.
2. **Two different entries added to one group, one in each copy**, through the same merges; and the
   same with a group added on both sides.
3. **An entry deleted in one copy** (deleted objects) through each program's merge and save: does the
   deletion travel, and does it beat an edit made after it in the other copy.
4. **History through merges and saves**: how many revisions a merge adds, whether merges apply
   `HistoryMaxItems` and `HistoryMaxSize`, what each other program keeps or drops of history when it
   saves, and what Database Maintenance's "older than" deletion does by default.
5. **Custom string fields and their protected flag through every other program's save**,
   KeePassDroid and KeePassDX 3.2/3.3 above all, since those delete node custom data.
6. **What the other programs show and let a user do to a store entry**: listing, search, editing,
   deletion, duplicate detection, health checks, auto-type.
7. Not run in the spike and still open for any store: KeePassXC's merge from its window, a reload
   of a file changed on disk, KeeShare and 2.8.0's remote sync; Keepass2Android's sync through its
   cache; KeePassDX releases between 3.3.0 and 4.5.8; a KeePass merge of a file another program merged.
8. Whether `NodeTagsTests`' tie rule and the other host tests named here hold on 2.62 as on 2.61.1.
   They are in the host tier, which the master gate runs (assumed passed, since #137 merged).

---

## Appendix: assumed statements in this report

Collected so none hides in a table: the KeePass result for different fields in one entry (4.1); both
entries arriving in KeePass when each copy added one (4.4); deletions travelling for entries in each
program (4.4); Database Maintenance using `MaintenanceHistoryDays` (4.2); D5's behavior under the
other programs (section 3); the consequences listed in 4.5 and section 7; the volume estimate in 1.4
(arithmetic on measured sizes); the host tests passing on 2.62 (9.8).
