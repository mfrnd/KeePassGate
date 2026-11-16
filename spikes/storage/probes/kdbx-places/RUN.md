# kdbx-places

Every place in a KDBX 4 or 4.1 file where a plugin could keep information, and what each desktop
program and library does to each place when it opens and saves a file, when it merges two copies,
and, for KeePass 2.62, when the key, the key derivation, the file name or the format version
changes. The report is `papers\ground\measured-kdbx-places.md`; every figure it quotes is in
`recorded\tables.md` and in the comparison files beside it.

## What it measures, and how

| Part | What | Where |
| --- | --- | --- |
| `probe\Kpl.csproj`, `probe\Program.cs` | KeePassLib 2.62: makes the bases with a value of the form `kpg:<place>:<variant>` in every place KeePassLib writes, and runs KeePass's own open and save, edit, `MergeIn` with each of the five methods, master key change, KDF change, Save As and the step down to 4.0 | this family |
| `scripts\kdbx_raw.py` | a reader and writer of the file format itself: both headers field by field, the decrypted XML with every protected value, the bytes after the last block | this family |
| `scripts\places.py` | `dump` (one line per place, from `kdbx_raw.py`), `inject` (adds the places KeePassLib cannot write), `save` and `cell` (the comparisons) | this family |
| `scripts\run.py` | runs every program on every base and writes the comparisons into `out\` | this family |
| `scripts\summarize.py` | turns `out\` into `out\tables.md`; the list of places, `PLACES`, is here | this family |
| `scripts\kw.js` | kdbxweb 2.1.1, with Node's own Argon2 handed to it, since kdbxweb brings none | this family |
| `scripts\pk.py` | pykeepass 4.2.0 | this family |
| `scripts\kp247.ps1` | KeePass 2.47's KeePassLib, in a process of its own | this family |
| `scripts\compare_runs.py` | holds a run's `out\` to `recorded\` | this family |
| `scripts\icon_chunks.py` | lists the PNG chunks of the custom icon of the `cap` step's files, and whether its text came through | this family |
| `android\make-bases.py`, `android\check.py` | the files and the check for the phone programs, section "For the phone programs" below | this family |

**The bases.** Five sets, each of five files made by `Kpl.exe make` and completed by
`places.py inject`: `a41` (Argon2d with 1 MiB and 2 iterations, KDBX 4.1), `e41` (AES-KDF with
60,000 rounds, 4.1), `a40` and `e40` (the same without any 4.1 feature, so KeePassLib writes 4.0),
and `s41` (Argon2d with 64 MiB, 200 iterations and parallelism 2, 4.1), the slow key derivation
`ground-keepassxc` used so that the two loads of a KeePassXC merge fall in different seconds. In
each set: `base`, `A-diff` and `B-diff` (every place with A's or B's value, A's times
2026-02-01 and B's 2026-03-01), `A-tie` and `B-tie` (the same values, both at A's times). A
holds a key, icon or record that B lacks and the other way round, in every place that is a
collection. `a41` and `e41` also hold `risky-hfield`, `risky-ifield` and `risky-eoh`: the base with
an outer header field of identifier 13, an inner header field of identifier 4, or bytes after the
end of header field's `0D 0A 0D 0A`, each in a file of its own because a reader that refuses it
would otherwise refuse every place at once.

**What `inject` adds** to every base, A and B: the outer header's comment (field 1); a binary in
the inner header no entry refers to; bytes after the last block; a marker in `Generator`; an
element `KpgX` at 18 positions of the XML; an attribute `kpgAttribute` on `Meta`, a `Group`, an
`Entry`
and a `Value`; an XML comment in `Meta`; and a `LastModificationTime` on a group's and an entry's
custom data item, which the format defines only for the database's. It decrypts the file with
the key, changes it and writes it again with the same seed, KDF parameters and inner stream key,
so every protected value stays valid.

**The programs.** Saves: KeePass 2.62 (`Kpl.exe resave` and `edit`, which adds an unrelated
entry), KeePass 2.47 (the same, `kp247.ps1`), `keepassxc-cli add -u xc-user <file> xc-added` of
2.4.2, 2.6.6, 2.7.12 and 2.8.0 (the command line has no plain resave, so the save is what an
unrelated `add` makes), kdbxweb `resave` and `edit`, pykeepass `resave` and `edit`. Merges:
`MergeIn` with Synchronize, Overwrite if newer, Overwrite existing, Keep existing and Create new
IDs on `a41`, `e41`, `a40` and `e40`; `keepassxc-cli merge -s <open> <arriving>` of the four
releases on `a41` and `s41`; kdbxweb's `merge` on `a41` and `e41`. Each merge runs four times: A
open and B arriving and the other way round, for the pair whose times differ and the pair whose
times are equal.

## Commands

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\kdbx-places\run.ps1
```

`run.ps1` builds the probe against the pinned KeePass 2.62, provides KeePass 2.47 and the four
`keepassxc-cli` releases through `tools\`, installs kdbxweb 2.1.1 from `kdbxweb\package-lock.json`
when it is absent, runs `scripts\run.py`, then `scripts\summarize.py`, then
`android\make-bases.py` into `work\android\`. `-Steps` runs a subset of `bases`, `saves`,
`merges`, `key_format` and `cap`. Python runs through
`uv run --no-project --with cryptography --with
argon2-cffi --with lxml`, pykeepass through `--with pykeepass==4.2.0`, both with
`UV_EXCLUDE_NEWER=2026-11-16T00:00:00Z`. Nothing shows a window.

To hold a run to the record:

```powershell
uv run --no-project --with lxml python -I -X utf8 spikes\storage\probes\kdbx-places\scripts\compare_runs.py spikes\storage\probes\kdbx-places\recorded spikes\storage\probes\kdbx-places\out
```

## Output

`out\` (fresh, not committed) and `recorded\` (the run the report quotes) hold:

- `bases\<set>.txt`: the dump of every input file;
- `saves\<program>\<set>-<file>.txt`: what a save did, one line per path that moved, after a line
  counting the paths and the paths kept;
- `merges\<program>\<set>\<pair>-<direction>.txt`: what a merge result holds of its two inputs,
  `AB` being A open and B arriving;
- `key_format\<set>-<operation>.txt`, `cap\<place>-<chars>.txt`;
- `tables.md`: every table the report quotes;
- `log.txt`: one line per operation, with its exit, its seconds and, when it failed, what it
  printed;
- in `recorded\` only: `android-places.txt`, the list of places of the phone files, and
  `icon-chunks.txt`, the output of `icon_chunks.py` over the second run's `cap` files, made by
  hand with the command below.

No `.kdbx` is kept; the databases are made in `work\` at every run.

The icon check, by hand after a run, for each size:

```powershell
$w = 'spikes\storage\probes\kdbx-places\work'
uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 spikes\storage\probes\kdbx-places\scripts\icon_chunks.py "$w\cap\M.icon.data\1024.kdbx" "$w\cap-out\keepassxc-2.4.2-add\M.icon.data\1024.kdbx" "$w\cap-out\keepassxc-2.6.6-add\M.icon.data\1024.kdbx" "$w\cap-out\keepassxc-2.8.0-add\M.icon.data\1024.kdbx"
```

## For the phone programs

Nothing here runs a phone program. What a later run of Keepass2Android and KeePassDX needs:

1. **The files**, from the branch root after `dotnet build
   spikes\storage\probes\kdbx-places\probe\Kpl.csproj -c Release` (or after `run.ps1`, which runs
   this as its last step):

   ```powershell
   uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 spikes\storage\probes\kdbx-places\android\make-bases.py --kpl spikes\storage\probes\kdbx-places\probe\bin\Release\net48\Kpl.exe --work spikes\storage\probes\kdbx-places\work\android
   ```

   It writes 20 files with the password `probe-only`, KDBX 4.1, Argon2d with 16 MiB, because
   KeePassDX refuses Argon2 memory below 8 MiB (read, `Argon2Kdf.getValidatedParameters`):
   `clean-base.kdbx` with every place KeePassLib writes; `clean-A-diff`, `clean-B-diff`,
   `clean-A-tie` and `clean-B-tie` for the merges, as the desktop sets hold them;
   `aes-clean-base.kdbx` with AES-KDF; one `place-<part>.kdbx` per place KeePassLib cannot write,
   that place alone on top of `clean-base`, so that a refusal names it (`comment`, `unreferenced`,
   `trailing`, `generator`, `xml-meta`, `xml-other`, `xml-empty`, `attrs`, `xml-comment`,
   `node-cd-time`, `hfield`, `ifield`, `eoh`); and `all-raw.kdbx`, the desktop programs' base. The
   source of KeePassDX and KeePassDroid refuses a comment field, any unknown outer header field
   with content and an unknown element in `Meta` (read), which is why each is in a file of its own.
2. **The list of places**, `work\android\places.txt`: per place its identifier, what it is, the
   file that holds it and the dump line of that file that shows it. `work\android\dumps.txt` is
   the dump of every file.
3. **The check**, for a file a phone program wrote:

   ```powershell
   uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 spikes\storage\probes\kdbx-places\android\check.py <file given> <file written>
   uv run --no-project --with cryptography --with argon2-cffi --with lxml python -I -X utf8 spikes\storage\probes\kdbx-places\android\check.py --merge <open copy given> <arriving copy given> <result written>
   ```

   One line per place: `kept`, `dropped`, `rewritten (to ...)` or `added (...)` after a save;
   `same`, `open`, `arriving`, `union`, `dropped` or `rewritten (to ...)` after a merge;
   `UNREADABLE` and the reason for a file the reader cannot open. It reads the files itself and
   never through a program.

## Runtime

The recorded run, on 2026-11-16, on a machine that other work kept at full load: `bases` 324.8 s,
`saves` 201.1 s, `merges` 1,119.4 s, `key_format` 3.9 s and `cap` 618.9 s, with the build,
`summarize.py` and `make-bases.py` about 45 s more: about 38 minutes. The slow key derivation is
most of `bases` and of `merges`: 16 KeePassXC merges of about a minute each, and every file of that
set read back by `kdbx_raw.py` at about ten seconds. The second run, the whole of `run.ps1`
with the code as it stands: `bases` 315.5 s, `saves` 366.6 s, `merges` 1,233.6 s, `key_format`
12.0 s, `cap` 669.7 s, 2,778 s in all:
about 46 minutes.

## The figures the report quotes

Every cell of the report's tables is a cell of `recorded\tables.md`, worded as section 3 of the
report explains, and `compare_runs.py` holds every comparison file and every cell of `tables.md` of
the second run to the record. Of 297 comparison files, 288 came out the same line for line, 7
differ and 2 exist in the record only (`android-places.txt` and `icon-chunks.txt`, which no run
writes into `out\`). The 7 are capacity files whose first line gives the size of the file, which
moved by 16 bytes, the cipher's block, either way; of 4,047 cells of `tables.md`, the 7 that
differ are those sizes. No outcome differs.

| Figure | Where in the report | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| places every save of every program kept, on the Argon2d 4.1 base (rows of the save table) | section 2, table | 63 of 117 | 63 of 117 | yes |
| positions of an unknown element dropped by every program but pykeepass | section 2, `X.*` | 18 of 18 | 18 of 18 | yes |
| saves of `risky-hfield` and `risky-ifield` refused by kdbxweb and pykeepass, both sets, resave and edit | section 2, second table | 16 of 16 | 16 of 16 | yes |
| deletion records for icons in kdbxweb's merge, A open, times differ | section 3 | 3 | 3 | yes |
| KeePassXC releases whose merge with the slow key derivation replaced the database's custom data | section 3 | 4 of 4 | 4 of 4 | yes |
| capacity places that all 8 programs kept at 1,048,576 characters | section 4.2 | 12 of 14 | 12 of 14 | yes |
| the longest capacity save, seconds (pykeepass, 1 KiB public custom data, in both runs) | section 4.2 | 33.1 | 57.4 | differs |
| files `make-bases.py` writes for the phone programs | section 6 | 20 | 20 | yes |
| every other cell of the report's tables | sections 2 to 4 | `recorded\tables.md` | `out\tables.md` | yes, but 7 file sizes, each 16 bytes apart |

## Cells that depend on a second boundary

- **KeePassXC's merge of the database's custom data** (`M.cd`, `M.cd.only`, `M.cd.time`) with the
  fast key derivation: the open copy's stays because both loads fall in the same second. A run in
  which a load straddles a second boundary would give the slow derivation's cell, the arriving
  copy's replacing the open copy's, for that release and pair. The slow set exists to show the
  other side.
- **Times a program stamps at the moment it runs**: `_LAST_MODIFIED`, the key times KeePassXC
  writes at a load, a group's `LastAccessTime` after a KeePassXC merge, `MasterKeyChanged` after a
  new master key, and the deletion records kdbxweb's merge writes for icons. `compare_runs.py`
  replaces each by `<now>` before comparing, so the second run is held to the record in everything
  but the second.

## Not run

- No phone program; section "For the phone programs" lists what a later run needs.
- No window of KeePass or KeePassXC: KeePass's merges are `PwDatabase.MergeIn`, which File then
  Synchronize and File then Import reach, and KeePassXC's are `keepassxc-cli merge -s`. What the
  windows write (`LastSelectedGroup`, `IsExpanded`, the KDF parameters at the database settings'
  OK) is read from source.
- KeePass 2.47's merge, pykeepass's merge (it has none), and kdbxweb on the 4.0 sets.
- A KeePass Synchronize of a file kdbxweb merged, which would show the deletion records it writes
  for unused icons removing those icons elsewhere.

## Changes made while recording

1. The recorded run's `bases`, `saves`, `merges` and `key_format` ran with the code as it stands.
   Its `cap` step ran first with values of a single repeated letter and a check that took any moved
   path holding a length for a moved place, which compression and KeePassXC's own additions made
   wrong: a 1 MiB value made a 4 KB file, and every KeePassXC save counted as moving the place.
   `Kpl.exe cap` now draws letters and digits from a fixed seed and `run.py` judges only the
   place's own path, `CAP_PATHS`; the `cap` step was run again alone with that code, and
   `recorded\cap\` and the `cap` lines of `recorded\log.txt` are that run.
2. `run.py` logs what a program printed only when it failed: on success it is a prompt and a
   message in the language of the machine. `recorded\log.txt` was trimmed the same way, and its
   machine paths replaced by `<branch>`, `<user>` and `<path>`.
3. `summarize.py` was changed after the run in how it words a cell (a refusal by the error's class
   alone, a merge cell `dropped` where the program's own save drops the place) and how it reads a
   capacity line; `recorded\tables.md` was written by the current `summarize.py` from the recorded
   comparison files, which it does not change.
