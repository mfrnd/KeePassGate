# What the probe settled

How KeePass 2.61.1 merges `PwDatabase.CustomData`, what that does to each way a revocation and a
certificate authority could be kept there, and what KeePassXC, KeePass 2.47 and the Android
programs that open KDBX 4 files do to the same keys, and to the rules on groups and entries, when
they save or merge the file. Every cell below but the rows section 2 marks as reasoned was
measured against throwaway databases the probe created, each read back with KeePassLib 2.61.1 on
.NET Framework 4.8; the output is under
[`spikes/revocation/output`](spikes/revocation/output) as it came back, so a re-run that disagrees
is a finding rather than a mistake. [`README.md`](README.md) says how to run it.

| Question | Answer |
| --- | --- |
| What decides a merge of a database's custom data? | The source's keys only, their modification times, the method and `SettingsChanged`. **Never the value, and no method removes a key the target holds** |
| Does a revocation written as an empty record survive a merge? | Only while both copies kept their times and the clocks agree. It loses to clock skew, to a copy saved without the times, and to Import with Overwrite existing |
| Does a separate key, added once and never changed, survive? | **Yes**, in every one of the 70 cells for every method, whatever was stripped |
| Can the authority be kept so that a merge never serves the wrong one? | **Yes**, with one key per authority and a retirement key per retired one, deciding on retirements rather than counting documents |
| Does KeePassXC keep any of this? | **No.** From 2.4.2 its merge replaces the database's custom data as a whole with the copy it loaded later. Its save keeps every key and value and drops their times, except from 2.7.0 in a file holding another KDBX 4.1 feature |
| And KeePass 2.47? | It opens a 4.1 file and saves every key and value without their times, as KDBX 4.0, or as 3.1 when nothing else needs 4.0 |
| And the Android programs that open KDBX 4 files? | No save and no merge that was run removed a key. Keepass2Android saves as KeePass 2.47 does, and its merge sets a key both copies hold to the arriving copy's value when that copy's `SettingsChanged` is later. KeePassDX keeps the keys and their times, and its merge puts the arriving copy's rule on a group or an entry whichever is newer. **KeePassDroid deletes every rule when it saves, and KeePassDX 3.2.0 and 3.3.0 every group's** |

**Nobody else's source is on this branch.** KeePass and KeePassXC are both GPL and this repository
is MIT. KeePass is cited by path and line in the `KeePass-2.61.1-Source.zip` release, KeePassXC by
path and line at its `2.7.12` tag. Both were measured, and their source read for why. So were the
three Android programs, GPL as well, each cited by path and line at the tag section 6 names.

## 1. The merge rule

`PwDatabase.MergeInDbProperties` (`KeePassLib/PwDatabase.cs:1541-1632`) is called from `MergeIn`
(`:934`) after the groups and entries are merged, so it runs for File then Synchronize and for every
File then Import, which merges what it read into the open database (`KeePass/DataExchange/ImportUtil.cs:204`
and `:306`). Its custom data loop is `:1602-1622`, and it iterates the **source's** keys:

- `KeepExisting` and `None` return before custom data is reached (`:1544-1545`). Nothing is copied.
- `OverwriteExisting` writes every source key with the **source's** time, and a source key without a
  time takes the target's time away with it.
- `Synchronize`, `OverwriteIfNewer` and `CreateNewUuids` are identical here, the last only giving
  groups and entries new identifiers first:
  - both stamped: the source wins only when **strictly** newer, so a tie keeps the target, and the
    file stores whole seconds;
  - only the target stamped: the target is kept;
  - only the source stamped, or the key absent from the target: the source wins;
  - neither stamped: the source wins when the target lacks the key or the source's `SettingsChanged`
    is newer, and the target is kept otherwise.
- **No method removes a target key.** A key only the target holds is never visited.
- **The value is never read.** Only the two times, the method and `SettingsChanged` decide.

Measured one key at a time, `output/rules.txt`, where `@new`, `@old` and `@none` are the time the
result carries:

| case | Synchronize | OverwriteIfNewer | OverwriteExisting | KeepExisting | CreateNewUuids |
| --- | --- | --- | --- | --- | --- |
| source only, stamped | S@new | S@new | S@new | absent | S@new |
| source only, unstamped | S@none | S@none | S@none | absent | S@none |
| target only, stamped | T@new | T@new | T@new | T@new | T@new |
| both stamped, source newer | S@new | S@new | S@new | T@old | S@new |
| both stamped, target newer | T@new | T@new | S@old | T@new | T@new |
| both stamped, equal | T@new | T@new | S@new | T@new | T@new |
| target stamped only | T@old | T@old | S@none | T@old | T@old |
| source stamped only | S@old | S@old | S@old | T@none | S@old |
| neither, settings equal | T@none | T@none | S@none | T@none | T@none |
| neither, source settings newer | S@none | S@none | S@none | T@none | S@none |

Three more facts from the same source. The public `StringDictionaryEx.Set(string, string)`
(`KeePassLib/Collections/StringDictionaryEx.cs:152-160`) stamps the current time even when the value
is unchanged; the only way to write a chosen time, or none, is the internal overload beside it
(`:162-173`), which the probe reaches by reflection to stand for a copy saved by a program that
drops the times. And `KdbxFile.GetMinKdbxVersion` (`KeePassLib/Serialization/KdbxFile.cs:351-397`)
chooses KDBX 4.1 for custom data only when some key carries a time (`:390-394`).

## 2. Five ways to write a revocation, against every method

`output/matrix.txt`. Each cell starts from one file holding a record `KPG.Client.h1` and
`KPG.Authority = X`, both stamped an hour ago, opened twice. The **revoker** revokes the client and
recreates the authority as Y; the **holder** is an untouched copy. Every step goes through a save
and a reopen: after the revocation, after stripping, and after the merge.

The conditions:

- **strip**: every custom data key rewritten with no time, which is what a save by KeePass 2.47 or
  by any KeePassXC measured leaves behind, unless the file holds another KDBX 4.1 feature and
  KeePassXC is 2.7.0 or later (section 5), and what a save by either Keepass2Android measured
  leaves behind (section 6);
- **skew**: the revoker's clock 60 seconds behind the machine that paired the client;
- **settings newer**: that copy's `SettingsChanged` is later, which any edit to a database setting
  does.

The designs:

- **Today**: the record written empty.
- **A**: the record written `revoked:<time>`, the time in the value only.
- **A+R**: A, and the plugin restamps any `KPG` key that lost its time from the time in its value,
  on the database it has open, before the merge.
- **B**: the record written empty and stamped `max(now, latest KPG time seen + 1 s)` through the
  internal overload.
- **C**: the record written empty, and `KPG.Revoked.h1` added and never changed or removed; admitted
  means a non-empty record and no such key. Recreating the authority also adds
  `KPG.RetiredAuthority.<X>`, and an authority with a retirement is refused.

A cell is the client's outcome and the authority served. `ADMIT` is the revoked client admitted by
the merge result; `X` is the old authority served, `Y` the new one, and `refuse` is C failing closed.

### Into the revoker (the revoking copy is open, the stale copy is merged in)

| method | condition | Today | A | A+R | B | C |
| --- | --- | --- | --- | --- | --- | --- |
| Synchronize | times kept | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| Synchronize | skew | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/Y | ok/refuse |
| Synchronize | revoker stripped | **ADMIT/X** | **ADMIT/X** | ok/Y | **ADMIT/X** | ok/refuse |
| Synchronize | holder stripped | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| Synchronize | both stripped | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| Synchronize | both stripped, holder settings newer | **ADMIT/X** | **ADMIT/X** | ok/Y | **ADMIT/X** | ok/refuse |
| Synchronize | both stripped, revoker settings newer | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| OverwriteIfNewer, CreateNewUuids | identical to Synchronize in every row | | | | | |
| OverwriteExisting | every condition | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/refuse |
| KeepExisting | every condition | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |

### Into the holder (the stale copy is open, the revoking copy is merged in)

A KeePass synchronize then writes the result to both files.

| method | condition | Today | A | A+R | B | C |
| --- | --- | --- | --- | --- | --- | --- |
| Synchronize | times kept | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| Synchronize | skew | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/Y | ok/refuse |
| Synchronize | revoker stripped | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/refuse |
| Synchronize | holder stripped | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| Synchronize | both stripped | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/refuse |
| Synchronize | both stripped, holder settings newer | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | **ADMIT/X** | ok/refuse |
| Synchronize | both stripped, revoker settings newer | ok/Y | ok/Y | **ADMIT/Y** | ok/Y | ok/Y |
| OverwriteIfNewer, CreateNewUuids | identical to Synchronize in every row | | | | | |
| OverwriteExisting | every condition | ok/Y | ok/Y | ok/Y | ok/Y | ok/Y |
| KeepExisting | every condition | not learned/X | not learned/X | not learned/X | not learned/X | not learned/X |

"Not learned": `KeepExisting` copies no custom data, so the holder never hears of the revocation.
Nothing is undone, and the revoking file is unchanged. The same holds for every design.

For C, in every one of the 70 cells, the added revocation key and the retirement were in the merge
result wherever the revoker's side took part (the `tombstoneKept` column), with four keys: record,
revocation, authority and retirement. **No KeePass merge lost one, and no record ever won over
one.**

The one `ADMIT/Y` in the A+R column is the restamp making things worse: restamping the holder's
record from the issue time in its value makes it the only stamped side, and it then beats the
unstamped mark.

### Rows reasoned rather than run

| scenario | Today | A | B | C | source |
| --- | --- | --- | --- | --- | --- |
| a KeePassXC merge, 2.4.2 or later, that loads the stale copy a second or more after the other | ADMIT/X | ADMIT/X | ADMIT/X | **ADMIT/X**, the added key removed | section 5, which measured design C's keys |
| the operator deletes `KPG.Revoked.*` under Database Maintenance, Plugin Data | n/a | n/a | n/a | ADMIT | a deliberate act |
| the authority deleted, then a synchronize with an older copy | everything returns | same | same | same | the rule: removals do not travel |

### Verdicts

- **Today's empty mark** fails on skew, on a stripped revoker, on both stripped with the holder's
  settings newer, on both stripped when the holder is the open copy, and on Import with Overwrite
  existing.
- **A, the time in the value**, is ruled out. KeePass never reads the value, so A is identical to
  Today in all 70 cells, and after a merge it loses there is nothing left to read. **A+R** fixes only
  a stripped revoker that is the open copy, and makes one case worse.
- **B, a logical clock in the modification time**, is partial. It fixes skew exactly in every method
  that reads times and nothing else, and it needs reflection into an internal KeePass overload.
- **C, a key added once and never changed**, holds against every KeePass 2.61.1 merge and import
  method: the client stays refused in every cell where the revocation took part, the authority fails
  closed wherever X came back, and Y is served wherever it did not. It fails under a KeePassXC merge
  that loads the stale copy later, as everything does, and under deliberate deletion.

## 3. One key per authority

`output/layout.txt`, the probe's `layout` question. `KPG.Authority.<fp>` holds the whole authority
document, a value constant per fingerprint; `KPG.RetiredAuthority.<fp>` is `retired`, constant and
never removed. Recreating retires every unretired authority, removes its document and adds the new
one; deleting retires and removes; a choice between several retires every other unretired one.
**Pruning**, on the open copy after the merge and then saved, removes `KPG.Authority.<fp>` wherever
`KPG.RetiredAuthority.<fp>` exists. The decision is taken on the unretired set after pruning: one is
served, more than one asks the operator, none serves nothing. "Ideal" is the union of the documents
on both sides less the union of the retirements on both sides.

The matrix is five scenarios, both directions, all five methods and seven conditions: times kept,
skew, P stripped, Q stripped, both stripped, and both stripped with either side's settings newer. P
is the copy that acted and Q the other. **The seven conditions gave the same result in every row**,
because every value under a given key is identical on both sides: the time rule only ever chooses
between equal values, and no method removes a target key, so presence is all that travels.

| scenario | into | Synchronize, OverwriteIfNewer, CreateNewUuids, OverwriteExisting | KeepExisting | keys |
| --- | --- | --- | --- | --- |
| (a) X recreated as Y on P, Q untouched | P | {Y}, correct (X came back and was pruned) | {Y}, correct | 2 |
| | Q | {Y}, correct (X pruned) | {X}, not learned, 1 key | 2 |
| (b) X recreated as Y on P and as Z on Q | P | {Y,Z}, operator asked | {Y}, not learned | 3 |
| | Q | {Y,Z}, operator asked | {Z}, not learned | 3 |
| (d) deleted on P, Q stale | P | {}, correct (X came back and was pruned) | {}, correct | 1 |
| | Q | {}, correct (X pruned) | {X}, not learned | 1 |
| (e) (b) merged into P and Y chosen, then the loser Q merged | P | {Y}, correct (Z came back and was pruned) | {Y}, correct | 3 |
| | Q | {Y}, correct (Z pruned) | {Z}, not learned, 2 keys | 3 |
| (f) X to Y to W on P, stale Q brings X back | P | {W}, correct (X came back and was pruned) | {W}, correct | 3 |
| | Q | {W}, correct (X pruned) | {X}, not learned | 3 |

No cell served a wrong authority (`WRONG` appears nowhere in the output), and no cell served
anything while the ideal set held more than one.

**Three copies** (c), recreated independently as Y, Z and W from one base, merged in all six orders,
in two shapes (`p<-q, p<-r` and `p<-q, r<-p`), with every method. Every method but `KeepExisting`
asked the operator after the first merge with two candidates and three keys, and after the second
with three candidates and four keys, in all 48 runs. A choice was then made on the result and each
loser, fresh from its own recreate, merged in both directions: all 192 of those merges ended with the
choice served and four keys, being the choice and the retirements of X and both losers.
`KeepExisting` learned nothing at any step, each copy serving its own, in 12 runs.

The key count is one live document plus one retirement per authority ever retired, and it never
shrinks. Documents exceed one only between a merge and its prune, or while the operator is being
asked.

## 4. Database Maintenance against pruning

`KeePass/Forms/DatabaseOperationsForm.cs:75` takes `CloneDeep()` of the database's custom data when
the form loads, and `:87-90` writes that copy back on Close when its key count differs from the live
dictionary's. The probe models both on the real types. `A` is `KPG.Authority.` and `R` is
`KPG.RetiredAuthority.`:

| case | copy at open | live at Close | written back | after Close | after the next prune |
| --- | --- | --- | --- | --- | --- |
| prune while open, retirement already there | A.X, A.Y, R.X | A.Y, R.X | yes | A.X back | Y served, repaired |
| recreate while open | A.X | A.Y, R.X | yes | **recreate undone** | **X served** |
| choice while open, {Y,Z} | A.Y, A.Z, R.X | A.Y, R.X, R.Z | no, the counts happen to be equal | choice kept | Y served |
| choice and prune while open, {X unpruned, Y, Z} | A.X, A.Y, A.Z, R.X | A.Y, R.X, R.Z | yes | **choice undone** | operator asked, 2 |
| R.X deleted by hand, X unpruned | A.X, A.Y, R.X | same | yes | A.X, A.Y | operator asked, 2 |
| R.X and A.Y deleted by hand, X unpruned | A.X, A.Y, R.X | same | yes | A.X | **X served** |
| R.X deleted by hand, X pruned | A.Y, R.X | same | yes | A.Y | Y served |

A prune on its own is undone into a state the next prune repairs, since the retirement is in the copy
too. What the write back loses is any retirement written while the form is open, by a recreate, a
delete or a choice, and whether it fires at all depends on a key count, so a prune beside a choice
is what turns a lucky "no" into a "yes". Pruning therefore belongs behind the same guard as every
other write, and the decision must never depend on a prune having run: it reads the retirements and
never counts documents.

## 5. KeePassXC and KeePass 2.47, measured

`output/keepassxc-save.txt`, `output/keepassxc-kdf.txt` and `output/keepassxc-merge.txt`, written by
`Measure-KeePassXC.ps1` and `Measure-KeePassXCKdf.ps1`. Six KeePassXC releases, each the Windows
archive its release publishes, pinned in `keepassxc.sha256` and signed by DroidMonkey Apps, LLC:
2.4.1, 2.4.2, 2.6.6, 2.7.0, 2.7.12 and 2.8.0. And KeePass 2.47, the last release before KDBX 4.1,
through its own KeePassLib in a process of its own. Measured on Windows 11 with a `de-DE` locale.

This section replaces an account read from the KeePassXC 2.7.12 source and never run. The
measurement confirms what that reading found of the merge, and corrects what section 2 took from
it: a KeePassXC save does not always drop the per-key times.

**The databases.** Two copies of a file, C and P, not to be confused with section 2's designs. C is
a paired database: two authorities, the first retired, two client records and an emptied record
beside its revocation, and a profile list, every key stamped with a time of its own; a group G with
a rule, and an entry E1 in it with another. P is C after a client was revoked, the second authority
retired and a third made, the profile list narrowed and both rules narrowed, every change stamped
later than anything in C, so P is newer than C in every way the plugin reads. In `P-plain` the
per-key times are the file's only KDBX 4.1 feature. `P-moved` also has an entry moved out of G by
KeePassLib, which records where it came from, and that previous parent is a second 4.1 feature.

### A plain save

`keepassxc-cli add`, a new entry at the root, which touches no custom data; and for KeePass 2.47,
`PwDatabase.Open`, an entry added, `PwDatabase.Save`, which is what its File then Save reaches.
**In all 35 cells all eleven keys came back with their values, and G and E1 kept their rules.**
What differs is the format and the times:

| program | `P-plain` | `P-moved` | `P-plain`, AES-KDF | no rules, Argon2d | no rules, AES-KDF | keys it adds |
| --- | --- | --- | --- | --- | --- | --- |
| KeePassXC 2.4.1 | 4.0, times gone | 4.0, times and previous parent gone | 4.0, times gone | 4.0 | 4.0 | none |
| KeePassXC 2.4.2, 2.6.6 | 4.0, times gone | 4.0, times and previous parent gone | 4.0, times gone | 4.0 | 4.0 | `_LAST_MODIFIED`, in G and E1 too |
| KeePassXC 2.7.0 | 4.0, times gone | **4.1, every time kept** | 4.0, times gone | 4.0 | 4.0 | `_LAST_MODIFIED`, in G and E1 too |
| KeePassXC 2.7.12, 2.8.0 | 4.0, times gone | **4.1, every time kept** | 4.0, times gone | 4.0 | 4.0 | those and `KPXC_RANDOM_SLUG` |
| KeePass 2.47 | 4.0, times gone | 4.0, times and previous parent gone | 4.0, times gone | 4.0 | **3.1** | none |

No file written as 4.0 or 3.1 kept a per-key time.

**Why KeePassXC from 2.7.0 writes `P-plain` as 4.0.** Loading sets the format from the file
(`src/format/KdbxReader.cpp:84`), and the header's KDF field then calls `Database::setKdf`
(`src/format/Kdbx4Reader.cpp:217`), which computes the format afresh, ignoring the format just read
(`src/core/Database.cpp:1092-1096`), while the tree is still empty. What it counts as KDBX 4.1
(`src/format/KeePass2Writer.cpp:52-108`) is a tag, a previous parent, an entry excluded from
reports or a custom icon with a name or a time, and never a custom data item's time, so the save
writes 4.0 unless the tree holds any of those, and the item times are written only at 4.1
(`src/format/KdbxXmlWriter.cpp:287-289`). A previous parent is what `P-moved` holds. Before 2.7.0 a
save drops the previous parent too, and writes 4.0 whatever the file held.

**KeePassXC's markers.** `_LAST_MODIFIED` is Qt's date text, with no time of its own, in the
system's language up to 2.7.12 and in English in 2.8.0. It is rewritten whenever a key is added or
changed (`src/core/CustomData.cpp:79-100`, writing it at `:177-188`), and loading adds every key
through that path (`src/format/KdbxXmlReader.cpp:443`), so the marker in memory is the time the file
was loaded, to the second, or the time of a change since. It is written into G's and E1's custom
data as well, the only nodes that had any, without changing their modification times.
`KPXC_RANDOM_SLUG`, in 2.7.12 and 2.8.0, is fresh random hex on every save
(`src/core/Database.cpp:311-313`), with a time of its own when the file is 4.1.

**KeePass 2.47** opened every 4.1 file and found all eleven keys. With AES-KDF and no custom data on
any node it wrote KDBX 3.1, which carries the database's custom data and no per-key times.

### The KDF change

No `keepassxc-cli` among the six changes an existing database's KDF: `db-create` takes a decryption
time for a new database, and `db-edit` in 2.7.12 and 2.8.0 sets credentials only. So
`Measure-KeePassXCKdf.ps1` drives KeePassXC's own window by UI automation: Database
Security, Encryption Settings, Advanced, transform rounds from 2 to 3, OK, with autosave on. The
rounds read back as 3 in all four files. KeePassXC 2.7.12 showed no window on two of its five
starts, and the script started it again.

| release | `P-plain` | `P-moved` | keys it adds beyond a plain save |
| --- | --- | --- | --- |
| 2.7.12 | 4.0, times gone | 4.1, every time kept | `KPXC_autosaveDelayMin=0` |
| 2.8.0 | 4.0, times gone | 4.1, every time kept | `KPXC_autosaveDelayMin=0`, `KPXC_REMOTE_SYNC_SETTINGS=[]` |

The change adds nothing to what a plain save does: the format is decided the same way, and the
format box shows KDBX 4 and is disabled.

### The merge

`keepassxc-cli merge -s <target> <source>`, which loads the target first. **Into P** is C merged
into P and **into C** the reverse. `fresh` is C as KeePassLib wrote it, `saved` is C saved once by
the same release first, so that it carries KeePassXC's marker on disk. `slow` is a KDF under which a
load takes well over a second, `fast` a KDF under which both loads fall in the same second, and the
`m` sets carry a previous parent in both copies, from 2.7.0, which keeps the result 4.1. A letter
per run names the copy whose `KPG` keys the target held afterwards, exactly and with nothing else.
216 runs, every program exiting 0.

| release | KDF | fresh, into P | fresh, into C | saved, into P | saved, into C |
| --- | --- | --- | --- | --- | --- |
| 2.4.1 | slow, fast | PPP | CCC | PPP | CCC |
| 2.4.2, 2.6.6 | slow | **CCC** | **PPP** | **CCC** | **PPP** |
| 2.4.2, 2.6.6 | fast | PPP | CCC | PPP | CCC |
| 2.7.0, 2.7.12, 2.8.0 | slow, slowm | **CCC** | **PPP** | **CCC** | **PPP** |
| 2.7.0, 2.7.12, 2.8.0 | fast, fastm | PPP | CCC | PPP | CCC |

- **From 2.4.2, the copy loaded later replaces the whole of the target's database custom data**,
  whenever the two loads fall in different seconds: all 96 `slow` and `slowm` runs. Into P, the
  result held exactly C's keys, the revocation `KPG.Revoked.<c2>`, the retirement
  `KPG.RetiredAuthority.<a1>` and the new `KPG.Authority.<a2>` removed and the record and the
  profile list set back to C's. Into C, it held exactly P's. A C saved by KeePassXC first, with its
  marker on disk, made no difference. `Merger::mergeMetadata` merges custom data only when the
  target has no `_LAST_MODIFIED` or the source's is later (`src/core/Merger.cpp:724-729`), and then
  removes every target key the source lacks (`:733-743`), sparing only the keys
  `src/core/CustomData.cpp:190-193` calls protected, and sets every differing source value
  (`:745-761`). The marker in memory being the load's time to the second, the later load is the
  newer.
- **Within one second it does nothing.** None of the 96 `fast` and `fastm` runs from 2.4.2 changed
  the database's custom data, in either direction, since neither marker is later than the other.
- **KeePassXC 2.4.1 merges no database custom data**, in any of its 24 runs.
- **Every merge from 2.4.2 saves the target** with `_LAST_MODIFIED` dated inside the merge, whether
  or not it changed anything.
- **The keys a merge copies carry the merge's time, not the source's.** In each of the 36 replaced
  4.1 results, every key the source brought in was stamped inside the merge, while the keys both
  copies held kept their own: the merge sets a value with no time (`src/core/Merger.cpp:758`), and a
  value set with no time is stamped with the current time (`src/core/CustomData.cpp:88-89`). After a
  replacing merge the stale values therefore carry the newest times in the file, which is what a
  later KeePass synchronize prefers (section 1). That later merge was not run.
- **A group on both sides keeps the target's custom data and takes the newer modification time.**
  G kept the target's rule in all 216 runs and came out with P's modification time, so into C the
  wider rule no longer looks older.
- **An entry takes the newer entry whole.** E1 came out with P's rule and P's modification time in
  all 216 runs, in either direction, with the older E1 in its history.
- **`--dry-run` exists from 2.6.6**, and in every run listed the five changes to the plugin's keys
  that the merge then made, or none where the merge made none.
- **The format afterwards** is 4.0, except the `slowm` and `fastm` results from 2.7.0, which are
  4.1.

### What this leaves the plugin

A KeePassXC merge from 2.4.2 that loads a stale copy a second or more after the other restores the
stale dictionary entire, an added revocation or retirement as readily as a record, so **no design
kept in `PwDatabase.CustomData` survives it**, and what KeePassGate can promise is limited to
KeePass's own merges. A KeePassXC save, or a KeePass 2.47 save, keeps every key and value; whether
the times survive depends on the release and on whether the file holds another KDBX 4.1 feature,
which is the strip condition of section 2.

### Not run

- Database then Merge in KeePassXC's window, with the change list 2.7.11 added; reloading a file
  changed on disk; 2.8.0's remote sync; KeeShare.
- KeePassXC 2.4.0 and earlier, 2.5.x and 2.7.1 to 2.7.11, so `KPXC_RANDOM_SLUG` first appears
  somewhere from 2.7.1 to 2.7.12.
- The KDF change on any release but 2.7.12 and 2.8.0, and KeePass 2.47's own window.
- The keys KeePassXC protects from removal, `KPXC_BROWSER_*` and `_CREATED_`.
- A month whose name differs between the system's language and English, in which the date marker is
  written up to 2.7.12.
- A KeePass 2.61.1 merge of a file KeePassXC merged.

## 6. The Android programs, measured

`output/android-save.txt`, `output/android-merge.txt` and `output/android-screens.txt`, with the
programs as they were verified in `output/android-programs.txt` and the databases they were given
in `output/android-base.txt`. Three programs, the Android programs that open KDBX 4 files, each as
the APK its project publishes on GitHub, pinned in `android.sha256` beside the digest of the
certificate that signed it. Keepass2Android 1.15-r6, the newest release not marked a prerelease on
the day of the run, and 1.08d-r5, the release before the notes of 1.09a-r3 add KDBX 4.1. KeePassDX
4.5.8, the newest that day, 3.2.0, whose notes list "Manage data merge", and 3.3.0, the release
after it, whose notes list "Merge from file and save as copy" and which was run for a save only.
And KeePassDroid 2.7, the newest that day.

Each was installed into an emulator of Android 11, API 30 on x86_64, started with no window under
Linux in WSL 2, and driven over `adb` by the scripts in `spikes/revocation/android`: `k.py` reads
the screen through `uiautomator` and presses what a flow file names. Every run starts from the
program's data cleared and its database alone in the device's download directory, opened through
the system's file picker, and what the program left there is pulled back and read by `Kpg.cs` with
KeePassLib 2.61.1. Eight attempts whose flow failed on the screen or was stopped were repeated from
cleared data once the flow was corrected, and are not counted: every save and every merge counted
below is a run whose flow ended without a failure.

**Measured and read are kept apart below.** A result is what a file read back as. A reason is read
from the program's source at the tag named and is marked as read from source, and nothing read
there is given as a result.

**The databases.** C and P as section 5 describes them, written again by `Kpg.cs`, so they have the
same shape and not the same bytes. `fast` is Argon2d and `aes` is AES-KDF, a set whose name ends in
`b` has no rule on any group or entry, and in `P-moved-fast` the entry E2 has been moved out of G
with its previous parent recorded. C and P of a set carry the same `SettingsChanged`; `C-fast-s`
and `P-plain-fast-s` are copies with a later `SettingsChanged`, for a merge in which the arriving
file's settings are the newer.

### A plain save

The file opened and unlocked, an entry `droid-added` added at the root and the database saved, each
by the program's own controls. Five sets for every release, and for KeePassDX 3.3.0 the two Argon2d
sets with rules: 27 files. **In all 27 all eleven keys came back with their values, and no program
added a key.** What differs is the format, the times and the rules:

| program | format | per-key times | previous parent | G's rule | E1's rule |
| --- | --- | --- | --- | --- | --- |
| Keepass2Android 1.15-r6, 1.08d-r5 | 4.0, and **3.1** with AES-KDF and no rules | gone | gone | kept | kept |
| KeePassDX 4.5.8 | 4.1 | kept | kept | kept | kept |
| KeePassDX 3.2.0, and 3.3.0 in its two sets | 4.1 | kept | kept | **deleted** | kept |
| KeePassDroid 2.7 | 4.1 | kept | kept | **deleted** | **deleted** |

**Keepass2Android strips as KeePass 2.47 does**, in both releases: the format column is the row
section 5 has for KeePass 2.47, and no file kept a per-key time or the previous parent. Read from
source at `v1.15-r6`: the reader hands each key's time to the dictionary
(`src/KeePassLib2Android/Serialization/KdbxFile.Read.Streamed.cs:639-640`), which keeps a time only
when it was made to remember them
(`src/KeePassLib2Android/Collections/StringDictionaryEx.cs:154-157`), and the dictionary a database
is left with when it is made was not
(`src/KeePassLib2Android/PwDatabase.cs:574`, against the field's own initializer at `:100`). So the
times are lost at the load and not at the save. With no time left, the format choice
(`src/KeePassLib2Android/Serialization/KdbxFile.cs:393-455`) answers 4.0 for Argon2d or for custom
data on a group or an entry and 3.1 otherwise; it has no case for a previous parent, which is
written only at 4.1 (`src/KeePassLib2Android/Serialization/KdbxFile.Write.cs:544-547` and
`:584-586`). At `1.08d-r5` the dictionary holds no times at all.

**KeePassDX 4.5.8 changed nothing the plugin reads.** It gave the root group the time of the save,
as 3.2.0 and 3.3.0 did, and the other two programs left that time alone.

**KeePassDX 3.2.0 and 3.3.0 delete a group's custom data.** G came back with none in all five files
that had a rule to lose, and with its modification time unchanged, so nothing about G shows that
the rule went. E1 kept its rule. The notes of 3.4.5 list "Fix custom data in group (fix KeeShare)";
no release between 3.3.0 and 4.5.8 was run, so where the deletion stops is not bracketed, and no
source line is cited for it.

**KeePassDroid deletes every rule.** G and E1 both came back with no custom data in the three files
that had rules, their modification times unchanged. Read from source at `v2.7`: the writer writes
custom data for the database and for nothing else
(`app/src/main/java/com/keepassdroid/database/save/PwDbV4Output.java:360`, the only use of that
element in the file), and the reader stores a group's custom data key in place of its value
(`app/src/main/java/com/keepassdroid/database/load/ImporterV4.java:880`).

**Two kinds of file read back with no `SettingsChanged` of their own.** All five KeePassDroid files
read back with the same `SettingsChanged`, the time at which they were read, later than every save;
so did the KDBX 3.1 file Keepass2Android 1.08d-r5 wrote, while its 4.0 files kept the value they
were given. The 1.15-r6 files were read before the reader printed that time. Read from source,
KeePassDroid's writer has no such element anywhere in `PwDbV4Output.java`. To a merge that compares
`SettingsChanged`, a file like these is the newer whenever it is read. That merge was not run.

### The merge

No program has a merge to call from a command line, so each was driven on its screen, and
`output/android-screens.txt` has what was pressed and what every dialog showed. **PC** is P open in
the program and C arriving, **CP** the reverse, on the `fast` sets with rules. Three runs a cell
but for the cell noted, 37 runs, and within a cell every run read back alike but for the time of
an entry the run added.

**Keepass2Android.** The merge reached is the merge its save offers when the file has changed
underneath it: with a database open, the arriving copy was written over its file, an entry
`droid-merge` added, and Save raised "Merge changes?", answered "Yes, merge". A trailing `s` is an
arriving copy with the later `SettingsChanged`.

| release | cell | open, arriving | keys only P holds | record and profile list | G and E1 |
| --- | --- | --- | --- | --- | --- |
| 1.15-r6, 1.08d-r5 | PC | P, C | kept | P's | P's rule, P's time |
| 1.15-r6, 1.08d-r5 | CP | C, P | added | **C's** | P's rule, P's time |
| 1.15-r6 | PCs | P, C with later settings | kept | **C's** | P's rule, P's time |
| 1.15-r6 | CPs | C, P with later settings | added | P's | P's rule, P's time |

- **A key only one copy holds is in the result, and no key is removed.** The revocation
  `KPG.Revoked.<c2>`, the retirement `KPG.RetiredAuthority.<a1>` and the new `KPG.Authority.<a2>`
  were in all 18 results, whichever copy was open.
- **A key both copies hold keeps the open copy's value, unless the arriving copy's
  `SettingsChanged` is later, and then takes the arriving copy's.** In CP the stale C was open, and
  the result held C's admitted record for the client beside P's revocation of it, and C's wider
  profile list. In PCs the newer P was open and the stale C arrived with later settings, and the
  record and the profile list went back to C's all the same. Nine runs of the 18 ended that way.
  The revocation still refuses that client. The profile list has no such key beside it: a name P
  had taken off the list was back on it.
- **Read from source**, at `v1.15-r6`. The merge is KeePassLib's `Synchronize` with the open
  database as the target (`src/Kp2aBusinessLogic/database/edit/SaveDB.cs:338`), in a port of
  KeePassLib that reports version 2.35 (`src/KeePassLib2Android/PwDefs.cs:69`). Its custom data loop
  copies a source key, with no time, when the target lacks the key or the source's
  `SettingsChanged` is later (`src/KeePassLib2Android/PwDatabase.cs:1476` and `:1530-1534`), which
  is section 1's case of neither side stamped and the only case left once every time is lost at the
  load. The target takes the source's `SettingsChanged` then (`:1480`), and the PCs and CPs results
  did read back with the arriving copy's. Synchronizing a remote file through its cached copy
  reaches the same `MergeIn` (`src/Kp2aBusinessLogic/database/SynchronizeCachedDatabase.cs:90`) and
  was not run.
- **Every result is KDBX 4.0 with no per-key time**, whatever the two copies carried.
- **G and E1 came out with P's rule and P's modification time in all 18 runs**, in either
  direction, E1 with one older version in its history.

**KeePassDX.** Two ways in 4.5.8, neither adding an entry. **from** is "Merge from ..." in the
database's menu, the arriving copy in a file of its own, which read back afterwards as it was
given. **ext** is the arriving copy written over the open file, which KeePassDX reports in a dialog,
and then "Merge data". **keep** is the same replacement with no merge asked for, an entry added and
the database saved, in one run. 3.2.0 was run through ext only, and there "Merge data" left the
file as it was until "Save data" was pressed, in all six runs.

| release | cell | open, arriving | the database's keys | G | E1 |
| --- | --- | --- | --- | --- | --- |
| 4.5.8 | fromCP, extCP | C, P | P's, at P's times | P's rule | P's rule |
| 4.5.8 | fromPC, extPC | P, C | P's, at P's times | **C's rule, P's time** | **C's rule, P's time** |
| 4.5.8 | keepPC | P, C | P's, at P's times | P's rule | P's rule |
| 3.2.0 | extCP | C, P | P's, at P's times | none | P's rule |
| 3.2.0 | extPC | P, C | P's, at P's times | none | **C's rule, P's time** |

- **The database's keys went by their own times.** All 18 merges ended with P's eleven keys at P's
  times in a 4.1 file, whichever copy was open.
- **A rule goes by no time: the arriving copy's replaces the open copy's, whichever group or entry
  is the newer.** With P open and the stale C arriving, G and E1 came out with C's older, wider
  rule under P's modification time in all six 4.5.8 runs, and E1 did in the three 3.2.0 runs, where
  G had no rule left to replace. The group or entry then carries the newer time and the older rule,
  so a later merge that goes by that time has nothing to show it the rule is stale. That later
  merge was not run.
- **Read from source**, at `4.5.8`, in
  `database/src/main/java/com/kunzisoft/keepass/database/merge/DatabaseKDBXMerger.kt`.
  `mergeCustomData` (`:415-432`) puts an arriving item when the target lacks its key, when either
  item has no time, or when the target's time is before the arriving item's, and it removes
  nothing. It is called for the database (`:268`), and for an entry (`:507`) and a group (`:577`)
  before their modification times are compared, and a rule is an item with no time, so a rule is
  always put. The comparison that would take a newer group compares the open group's time with
  itself (`:584`). At `3.2.0` the same function is
  `app/src/main/java/com/kunzisoft/keepass/database/merge/DatabaseKDBXMerger.kt:306-320`. By the
  same reading, an arriving copy whose per-key times were dropped by another program's save would
  have its value put for every key both copies hold, the stale value as readily as the fresh. That
  merge was not run.
- **A replacement that does not move the file's time on was not noticed.** In keepPC the arriving C
  was written over the open file with its own older time: no dialog was shown, and the save wrote
  the open P and the added entry over it. Read from source, a change is reported when the file's
  time is more than ten seconds past the time held for it
  (`app/src/main/java/com/kunzisoft/keepass/services/DatabaseTaskNotificationService.kt:219`), which
  is why the ext flows wait eleven seconds and then set the file's time.

**KeePassDroid** showed no merge to run. Its menu holds About and Settings before a database is
open and Donate, Settings, Change master key and DB sort order after, and its settings screen
Database and Application. The page behind Database was never listed, the last press of that flow
having failed. Read from source, the strings at `v2.7` (`app/src/main/res/values/strings.xml`) name
no merge and no synchronization.

### What this leaves the plugin

Design C held. The three keys only P holds, the revocation, the retirement and the new authority,
were in all 37 merge results, and no save and no merge removed a key from the database's custom
data: none of these programs does what a KeePassXC merge does. What they reach is everything design
C does not cover. A key that is changed and not added, the admission record or the profile list,
came back at its older value from a Keepass2Android merge, with the stale copy open or with the
stale copy arriving under later settings; the record is refused all the same by the revocation
beside it, and the profile list has nothing beside it. A Keepass2Android save is the strip
condition of section 2. And a rule on a group or an entry has no protection at all: a KeePassDX
merge brings an older rule back under a newer time, KeePassDroid deletes every rule in a file it
saves, and KeePassDX 3.2.0 and 3.3.0 every group's, none of them moving the modification time of
the group or entry. A deleted rule refuses nothing. The group or entry then answers with what it
inherits or with the default, so where the rule narrowed what a group above grants, deleting it
widens what a client reaches.

### Not run

- Keepass2Android synchronizing a remote file through its cache; its setting to merge on every
  conflict without asking (`src/Kp2aBusinessLogic/database/edit/SaveDB.cs:162`); the releases
  between 1.08d-r5 and 1.15-r6.
- A Keepass2Android merge of a file that carries no `SettingsChanged`, as a KeePassDroid save
  leaves it. Read from source, a database is given the current time when it is made
  (`src/KeePassLib2Android/PwDatabase.cs:543`), so such a file would arrive with the later
  settings.
- KeePassDX 3.3.0 in a merge, and every release between 3.3.0 and 4.5.8, whose notes list merge
  fixes at 4.1.8, 4.2.1 and 4.2.2; reloading and saving in answer to 4.5.8's dialog in place of
  merging; a merge of a copy without per-key times.
- KeePassDroid's database settings page.
- A merge of `P-moved`, or under AES-KDF or a slow KDF, and later settings in any program but
  Keepass2Android 1.15-r6.
- A KeePass 2.61.1 merge of any file these programs saved or merged.
- Any Android release but 11, any image but x86_64, and a device in place of the emulator.
