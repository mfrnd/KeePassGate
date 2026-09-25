# What the probe settled

How KeePass 2.61.1 merges `PwDatabase.CustomData`, and what that does to each way a revocation and
a certificate authority could be kept there. Every KeePass cell below was measured on .NET Framework
4.8 against throwaway databases the probe created; the output is under
[`spikes/revocation/output`](spikes/revocation/output) as it came back, so a re-run that disagrees
is a finding rather than a mistake. [`README.md`](README.md) says how to run it.

| Question | Answer |
| --- | --- |
| What decides a merge of a database's custom data? | The source's keys only, their modification times, the method and `SettingsChanged`. **Never the value, and no method removes a key the target holds** |
| Does a revocation written as an empty record survive a merge? | Only while both copies kept their times and the clocks agree. It loses to clock skew, to a copy saved without the times, and to Import with Overwrite existing |
| Does a separate key, added once and never changed, survive? | **Yes**, in every one of the 70 cells for every method, whatever was stripped |
| Can the authority be kept so that a merge never serves the wrong one? | **Yes**, with one key per authority and a retirement key per retired one, deciding on retirements rather than counting documents |
| Does KeePassXC keep any of this? | **No.** Its merge replaces the database's custom data as a whole, read from its 2.7.12 source |

**Nobody else's source is on this branch.** KeePass and KeePassXC are both GPL and this repository
is MIT. KeePass is cited by path and line in the `KeePass-2.61.1-Source.zip` release, KeePassXC by
path and line at its `2.7.12` tag. The KeePass rule was read from the source and then measured; the
KeePassXC rows were read and not run.

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

- **strip**: every custom data key rewritten with no time, which is what KeePassXC 2.7.12 and KeePass
  2.47 and earlier leave behind;
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
| a KeePassXC 2.7.12 merge from a stale copy | ADMIT/X | ADMIT/X | ADMIT/X | **ADMIT/X**, the added key removed | section 5 |
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
  closed wherever X came back, and Y is served wherever it did not. It fails under a KeePassXC merge,
  as everything does, and under deliberate deletion.

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

## 5. KeePassXC 2.7.12, read from its source

Read at the `2.7.12` tag and not run.

**Its merge replaces the dictionary.** `src/core/Merger.cpp:724-762` (`Merger::mergeMetadata`)
merges the database's custom data as a whole, and only when the target has no `_LAST_MODIFIED` key
or the source's is newer. When it does, it **removes every target key the source lacks** (`:733-743`),
sparing only the keys `src/core/CustomData.cpp:190-193` calls protected, and then copies every
differing source value (`:745-761`).

**And the source is normally the newer one.** Every `set` that adds or changes a key rewrites
`_LAST_MODIFIED` to the current time (`src/core/CustomData.cpp:79-100` and `:177-188`), and
`src/format/KdbxXmlReader.cpp:443` loads each item through that `set`, so a database's
`_LAST_MODIFIED` in memory is the time it was loaded rather than the time stored. A copy loaded at
merge time is therefore newer than a target opened earlier.

**It writes both markers on every save.** `Database::saveAs` (`src/core/Database.cpp:312-314`),
which every save reaches, first sets `KPXC_RANDOM_SLUG` to fresh random bytes, so `_LAST_MODIFIED`
changes on every save too. `src/format/KdbxXmlWriter.cpp:264-276` writes every key without a
filter, and `:287-289` writes per-key times only at KDBX 4.1. Opening a database without editing it
writes nothing (`src/core/Database.cpp:181`).

Unknown keys, `KPG.*` among them, are otherwise kept on load and save: nothing on either path filters
them. The consequence is that a KeePassXC merge of a stale copy into a revoked one restores the stale
dictionary, removing an added revocation key as readily as it restores a record. **No design kept
inside `PwDatabase.CustomData` survives it**, which is why what KeePassGate can promise is limited to
KeePass's own merges.
