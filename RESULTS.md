# What the probe settled

How KeePass 2.61.1 merges `PwDatabase.CustomData`, what that does to each way a revocation and a
certificate authority could be kept there, and what KeePassXC and KeePass 2.47 do to the same keys
when they save or merge the file. Every cell below but the rows section 2 marks as reasoned was
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

**Nobody else's source is on this branch.** KeePass and KeePassXC are both GPL and this repository
is MIT. KeePass is cited by path and line in the `KeePass-2.61.1-Source.zip` release, KeePassXC by
path and line at its `2.7.12` tag. Both were measured, and their source read for why.

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
  KeePassXC is 2.7.0 or later (section 5);
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
