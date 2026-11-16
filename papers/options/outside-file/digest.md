# Digest: `outside-file`

Checked 2026-11-15 against `option-outside-file.md`, both `critique-outside-file-*.md`,
`measured-android.md` and `ground-inventory.md`. **M** measured ([M design] the design's console
probe, [M critic] the survival critic's, "model" its executable model of the design's text, no real
synchronizer); **R** read from source; **A** assumed.

## 1. What it is

The authority document and a new 32-byte store key stay in the database's own custom data, written
once and never changed. Everything that changes moves to a folder beside the file,
`Passwords.kdbx.kpg`: one file per gate installation ("writer"), sealed with AES-256-GCM under the
store key. At load the plugin joins those files with no clock: any writer's revocation holds, a
removed name stays off, the more refusing answer wins, a rule changed on two computers unknowingly
refuses. Each file records how far it saw the others, a per-computer "seen" file the newest loaded,
and anything missing, changed or older answers `503 undecided`. Nothing is on a node, no store
write saves the database, the API does not change. **Covers all data**: the authority in the file,
data 2 to 7 in the folder.

## 2. The hard requirement

Other programs reach only the two keys and the tree. That no `.kdbx` program opens the folder is
A in the design, with no counterexample in any program the ground reports read [R].

| Program | Save | Edit, delete, move | Copy | Merge | Back into KeePass | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| KeePass 2.62 | keys kept [M design] | rule keyed by UUID, kept [R design] | **Duplicate: new UUID, no rule**; today it travels [R 2.62 `PwEntry.cs:856-864`, `PwGroup.cs:1437-1446`] | keys never removed, 38 of 40, the 2 others Keep existing into a keyless copy [M design, KeePassLib]; Synchronize carries no folder [R 2.62] | n/a | **fails open** |
| KeePassXC 2.4.2 to 2.8.0 | kept, times dropped [M design] | untouched | Clone: no rule [A] | a stale copy holding any other key, loaded a second or more later, removes both keys, 8 of 8 [M design]; 2.7.12 and 2.8.0 add two keys to every save, so any pre-Create copy they saved does it, 2 of 2 [M critic]; deletions never applied [M keepassxc 4] | a KeePass Synchronize restores the keys [M design, KeePassLib] | **fails closed** at every stale merge |
| Keepass2Android 1.15-r6 | kept, per-key time dropped [M android] | kept, UUIDs kept [M android] | not measured | both holding: kept [M android]; one holding: added, 18 of 18 [M spike]; resurrects an entry changed after its deletion [M android X2] | kept both ways [M android] | **survives with a named change** (P3) |
| KeePassDroid 2.7 | kept with time [M android] | edit, delete kept; no move [M android] | not offered [M android] | none; every save over a changed file overwrites [R android] | kept both ways [M android] | **survives with a named change** (P3) |
| KeePassDX 4.5.8 | kept [M android] | kept [M android] | not measured | kept; drops arriving deletions unrecorded [M android X1]; 10 s overwrite [M spike] | exact both ways [M android] | **survives with a named change** (P3) |

The KeePassXC and phone verdicts take P3's change as applied (never remove a rule for an absent
node: it deletes a button and was not run); without it each fails open through P3. They assume one
gate computer and that their copy commands, unmeasured, do not exist; a copy with a new UUID is
item 1 everywhere. P9 (section 3, item 6) runs through KeePassXC and every
overwrite with two gate computers, or on one after a stop in the middle of a Recreate.

**What `measured-android.md` changes**: the Android rows for the keys, spike or read until now, are
measured on these releases, the way back included; so are the whole-entry merge [R android 1.1] and
the resurrections P3 rests on, Keepass2Android added. **It contradicts no survival claim of the
design or its critics.** Still unmeasured: overwrites, phone copy, phone display, a phone
synchronizer carrying the folder. Aside: KeePassDroid and KeePassDX kept a 4.0 file 4.0, and
Keepass2Android wrote one as 3.1 with the keys kept [M android], against the hardened-custom-data
critic's "every KeePassDX and KeePassDroid save writes 4.1".

## 3. Sequences that end wider or undo a revocation

1. **Copy escapes its rule** (P5, security 2b): duplicate a node narrower than its group; the copy
   inherits. One step [R 2.62]. Fix: a write-once marker naming the rule's UUID, mismatch refuses.
   Not run; a KeePassDroid save deletes the marker with all node custom data [M android], so copies
   made after it escape again.
2. **Tidy, then resurrection** (P3): delete a narrowed node, remove its orphan rule on the Store
   page, then KeePassXC [M keepassxc 4], KeePass [M keepass X3, X4], Keepass2Android [M android],
   KeePassDX then KeePass [M android] or an overwrite [R] brings it back. Fix: never remove; write a
   refusing value. Not run.
3. **Node before its rule** (security 2a): two gates; the file arrives before the folder [R design;
   A lag]. Uncloseable here.
4. **Fast clock** (P2): B revokes A's client, B's clock passes `notAfter`, B's next write drops the
   revocation, A's record line admits [M critic, model]. Fix: keep it while any live record line
   exists, plus a margin. Not run.
5. **One writer name, two computers** (P1): a copied configuration carries `KPG.Writer`
   [R 2.62 `AppConfigSerializer.cs:131-165`]; the synchronizer renames one version; the revocation
   survives only in the conflict copy, which nothing reads [M critic, model; synchronizer A]. The
   design's 21 checks never covered it. Fix: a per-computer local name; a conflict-named file
   closes the store. Not run.
6. **Recreate reached backwards** (P9): `KPG.Closed` has not reached a second computer, or the first
   stopped before writing it; a stale merge or overwrite restores the old keys; the leaked authority
   serves [R design; replacement M spike, M design]. Fix: refuse a store older than one served here;
   retire the old authority in the old store before saving. Not run.
7. **Create carries older rules and names** (P4) after a lost key [R design]. Fix: carry only from
   a passing store, never names. Not run.
8. **One file rolled back**, on a computer with no seen file (P8) [M critic, model]. No fix.
9. **A restore with its seen file, or onto a new computer** (P7): as today [R]. No fix.
10. **Leaked store key plus folder access**: wider rules until Recreate, no revocation undone
    [M design]. Worse than today.

Shared: **a whole file over a newer one** loses nothing in the folder (the real gain); the tree
reverts, feeding 2 and 6. **A move undone by a merge**: nothing done; KeePassDX undid one
[M android Y3].

## 4. Refusals in ordinary use (`503 undecided`)

- File copied or moved without the folder, or Save a copy: until the folder follows or is chosen.
- A second gate sharing only through KeePass Synchronize: always, until a shared folder is set up.
- Every KeePassXC stale merge: until a KeePass Synchronize with a copy holding the keys.
- A writer file missing, older, edited, a placeholder, a share away: restore it, or Recreate. A
  reader not sharing deletion fails the write [M design, .NET 11 only].
- Older than this computer saw, a writer collision, two stores: one press on the Store page.
- A rule changed on two computers: that subtree, until settled in the grant editor.
- More than 16 writers: for good; Recreate.

## 5. Costs

- **Seen**: KeePass, two keys under Plugin Data and a fourth KPG page; other programs, nothing
  [A]; a cloud account, the folder, a clear `store.json` naming KeePassGate, one file per gate.
- **Keys**: the authority key unchanged; the store key is a lasting forge ability for anyone who
  once opened the database and can write the folder; KeePassXC's merge report prints removed keys
  with values [M critic].
- **Growth**: 81,764 bytes for 200 rules, 30 records, 30 revocations [M design]; writers and
  retirements only accumulate.
- **Per request**: 50 µs look, 1.9 ms reload, 18.6 ms write, local NTFS [M design]; shares
  unmeasured; an equal-length change can be missed [A].
- **Classes**: seven new store classes; owed answers, `ProfileNames.Merge`, the `m_sdCustomData`
  reflection and `500 not verified` for store writes go; `DatabaseWriter`, `OpenDatabaseTargets`
  (nine `503` states) and the buffers change; Create and Recreate commit across file and folder;
  `GroupDialogs` stops guarding plugin data.
- **Grant editor**: a buffer applied at OK, no entry revision, its own save question.
- **Format**: 4.0 suffices; KeePassXC stamps untimed keys [M hardened-custom-data critic].

## 6. Settled decisions reversed

"No sidecar", "stored inside itself", "no external state" (E9, `CA.md` §202); the database
`CustomData` bullet ("only ever added" becomes "held while any writer asserts it"); KDBX 4.1; grants
in node custom data, and "a copy carries its rules"; writing into `m_sdCustomData`; `KPG.Profiles`
as a difference; owed answers; "nothing written to the disk" and no cipher of our own, for state;
"no credential before its record is in the file" (now the store); the trap section, for plugin
data; two gates over KeePass Synchronize alone (`DESIGN.md` §113).

## 7. Verdict

For the phones this is the strongest option: only two write-once keys reach them, and every Android
program kept those through everything measured and back into KeePass. KeePassXC fails closed. It
still fails open in KeePass itself, in one step, by duplicating a narrowed node. On one gate
computer sequences 2, 6, 7, 9 and 10 remain; a second adds 3, 4, 5 and 8, and 3 cannot be closed.
No fix was run. The security critic's hybrid (rules in the file, only add-only refusals outside)
has no gap 3 and is unevaluated.

**Measure first**: one writer name on two computers under Syncthing, Dropbox and OneDrive; the
fixes for 1, 2, 4 and 6, implemented; phone copy commands; a KeePassXC window merge, then KeePass's
Synchronize; overwrites by a pre-Recreate copy; `File.Replace` on .NET Framework over FAT32 and SMB.
