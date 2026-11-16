# Digest: option `single-entry`

From the design, its two critics ("survival", "security"), `measured-android.md` and
`ground-inventory.md`. M measured, R read from source, A assumed, with whose probe.

## 1. What it is

One ordinary entry, under a fixed public identifier in a `KeePassGate` group with searching and
auto-type off, holds the whole state as one protected custom string. The plugin rewrites it in
place with no history item, so every merge takes it whole. Because the whole is chosen by
modification time, each version carries an identifier, its last 256 ancestors and a stamp per
datum; a step on every read weighs the current value, the history and the plugin's memory, then
restores the newest or merges two heads, narrowing where both changed. A witness entry per version
turns a lost version into `503 undecided`, revocations and retirements are also add-only entries of
their own, and copies are caught only inside "closed" groups that list their children. It covers
all data, database-level and rules.

## 2. The hard requirement

No merge in any program left a mixture [M design, 128 cells; M measured-android]. With one plugin
host a phone's copy is an ancestor [A, survival].

| Program | Verdict | Why |
| --- | --- | --- |
| KeePass 2.62 | fails open | merges end complete or refused [M design], but section 3's items 1, 2, 6 and 7 end wider; ties without the plugin, Keep existing, history limit 0 refuse [M design]; a deleted state entry is gone from both copies after Synchronize [M design] |
| KeePassXC 2.4.2 to 2.8.0 | fails open, by the shared move regression only | its own data comes through or refuses: ties in 16 of 64 cells [M design]; deletions never applied, so witnesses pile up and past 256 versions the gate refuses falsely [M survival W2] |
| Keepass2Android 1.15-r6 | fails open, by "No, overwrite" only | [R ground-android 4.2]. All else as the design read [M]: strings, flags, a 2,861-character value through save, edit, move, delete and back; the older version whole in history; at a tie the arriving copy wins and the other is nowhere (a refusal); witnesses arrive; delete recycles first |
| KeePassDroid 2.7 | fails open | save, edit, delete keep every string and flag [M]; no move or merge [M]; an unchanged Save makes a new version [M], handled. Every save over a changed file writes its copy whole [R `SaveDB.java:54-75`], not run |
| KeePassDX 4.5.8 | fails open | saves and edits keep everything [M]; merges as Keepass2Android but a tie keeps the open copy [M]; arriving deletions leave no record [M], so witnesses return, recognized within the window; overwrites within 10 s [R; M spike] |

What `measured-android.md` changes:

- It contradicts no phone read of the design's; they are now measured for the shape, not for a
  20 to 65 KB entry.
- It qualifies the survival critic's 2.5: KeePassDX undoes a move at equal times [M], but a KeePass
  Synchronize whose copy holds the move restores it [M, 5.3]; the gate sees it only after a
  reload [A].
- Still open: screens, KeePassDroid's overwrite, history trimming. The design's "dots on the
  phones" [A] is contradicted for KeePassDroid by source [R security, `EntryActivityV4.java:60-70`].

## 3. Sequences that end wider, shortest first

1. **One merge of a crafted file** adding a rule on a node with none, below a seal (Synchronize or
   Overwrite if newer either way, Keep existing with the file earlier): the step takes it as a
   concurrent head [M security S1, the design's own code]. The design claims the opposite. The same
   rule should re-add a removed profile [A]. Change: a MAC under the gate key; not run.
2. **A copy of a node whose own rule narrows**, into a group not closed: it inherits [R 2.62]. Today
   it carries the rule [M rules-central critique]. Change: close the root, or rules-linked's
   back-reference; neither run here.
3. **Shared, a whole file over a newer one** (KeePassDroid's save, "No, overwrite", a KeePassDX
   save under 10 s after a change, a synchronizer), then KeePass locks and unlocks: a revoked
   client admitted, no sign [R ground-android 4.2; M spike; A reload]. Change: a rollback marker outside the file, reversing
   E9; not built.
4. **Shared, a move undone**: move into a sealed group, another copy edits the entry, KeePassXC
   merge, KeePass Synchronize [M ground-keepassxc 4, 9]. Change: closed lists pin parents; not run.
5. **Conversion from 0.1.0**: removed keys travel nowhere [M spike], so a 0.1.0 plugin serves the
   old authority without later revocations [R wildcard security 3.9]; design 13 says the opposite.
   Change: write `KPG.RetiredAuthority.<fp>` at conversion; not run.
6. **Continue**: a same-second tie merged without the plugin, Continue, then an older copy
   synchronized in: the narrowing is discarded [M survival C1]. Change: Continue acknowledges,
   not subsumes; run, restores both.
7. **A copy over 256 versions behind**: a removed profile returns, or a concurrent narrowing is lost
   silently [M survival W1, W1b]. The design's probe used a window of 64 and stopped at 25
   generations. Change: a version vector; not run.
8. **Shared**: a stale copy under a second plugin host serves what it holds [design 12].

## 4. Refusals in ordinary use

- `503 undecided`, whole database: a tie merged without the plugin, Keep existing, history limit 0,
  an oversized document [M design]; a KeePassXC-merged file past 256 versions [M W2]; one crafted
  witness [M security]. The operator synchronizes with the copy holding the lost version, or
  presses Continue on a fourth KPG page, losing what it changed.
- Nothing served when the state entry is in the bin, deleted, damaged, or removed by Delete
  Duplicate Entries after a Create new IDs import [M both critics]. Remedy: the bin, Put it back,
  Restore from a file, a KeePassXC merge; not a plain synchronize [M design].
- Closed groups: a node made on a phone, by Add Group, or moved in, waits for a confirmation on
  that group's KPG tab. A narrowing under the root closes the root, so in practice every
  phone-added entry [survival 2.8; frequency A].
- A returning node on the `gone` list; `503 busy` while an entry dialog holds the state entry.

## 5. Costs

- Seen: one group, the state entry, a witness, an entry per revocation and retirement. Hidden until
  revealed in KeePass and KeePassXC [M]; reveal and copy in Keepass2Android [R]; **clear text in
  KeePassDroid** [R]; KeePassDX [A].
- The authority key, host PKCS#12 and gate key move from custom data, which only XML export shows,
  to a string KeePass's Copy, print and KDB export and KeePassXC's HTML export write in clear
  [R; M ground-keepassxc 8]. Unavoidable in one entry.
- Growth: 20 to 65 KB rewritten whole at every pairing, renewal, revocation, rule or profile edit,
  plus 16 characters per child of a closed group; one deletion record per version forever
  [M survival]; witnesses pile up in KeePassXC-merged files [M].
- Per request: look-up 0.03 to 0.12 ms among 10,002 entries [M design]; the C# parse and merge
  not measured.
- Classes: `StateDocument` and `StateStore` arrive, `CustomDataStore` goes, about twenty change.
- Grant editor: off `m_sdCustomData`, buffered to the dialog's OK, which KeePass's Escape question
  cannot see [R]; narrowing closes the parent. Tabs: a fourth page, a confirm list on group tabs.
- Gain: Database Maintenance and node custom data deletions [M] touch nothing.

## 6. Reverses

The database's own `CustomData` as the store, with per-key times and add-only keys; "the authority
takes the file to KDBX 4.1"; one rule document per node under `KPG`; the grant editor writing into
`m_sdCustomData`; "the plugin writes no rule of its own"; a copy carries its rules; `KPG.Profiles`
replayed as a difference; `503 undecided` with one cause; three tab pages; absence always silence.
The critics' changes add "nothing in a record is authenticated beyond the file" and "no sidecar".

## 7. Verdict

The premise is now measured everywhere: a merge takes the one entry whole. But whole means chosen
by time, so the shape needs lineage, history, memory, witnesses and refusals to avoid serving a
stale state, and its critics measured three ways of its own to end wider; only the Continue fix
was run. Its costs are the steepest: the authority key shown in clear by KeePassDroid, the whole
gate stopping on one deletion, a refusal at every tie merged elsewhere. On the shared overwrite it
does no better than any in-file store, and by its own section 10 `wildcard` buys "one state, taken
whole" without history or ties. Not as written. Before trusting it: KeePassDroid's save over a
changed file, then a KeePass reload; the MAC and a version vector over S1, W1, W1b, W2 and C1
beyond 1,000 versions; a 65 KB protected string through each phone's save, edit and entry screen;
Keepass2Android's history trimming at its limit; `FileSavingPre` after a merge and the entry
dialog's OK on the state entry, in KeePass's window.
