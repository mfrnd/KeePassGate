# ground-inventory: the probe and what it reproduces

The probe behind `papers/ground/ground-inventory.md`, the inventory of what KeePassGate stores in
a database, which names this directory as the place of `sizes.ps1` and `sizes.txt`. The record is
`recorded\sizes.txt`.

| Probe | What it measures | Cited by |
| --- | --- | --- |
| `sizes.ps1` | the lengths of what the plugin stores, written by the plugin's own code over an in-memory database: one authority, one invented client with two profiles, profile lists, two rules | `ground-inventory.md` §0 items 2 and 6, §1.1, §1.2, §1.4, §8 |
| `constants.ps1`, added here | KeePass 2.62's history defaults and a new database's maintenance period, read by reflection | `ground-inventory.md` §0 item 6, §4.2, §8 |

Both ran. Every length the report quotes comes out again on a build of the plugin at `a42e38aa`,
with one length that varies from run to run: the client certificate's, which the record shows at
487 bytes and this run at 489. The authority document varies too, between two lengths, and this
run gave the recorded length. The history defaults reproduce. Several sizes in the report's tables
are arithmetic rather than measured; they are listed apart and agree with the product's key
formats.

## Running it

From the branch root, in Windows PowerShell 5.1, which is what the probe was measured with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-inventory\run.ps1
```

`run.ps1` provides and checks KeePass 2.62 through `tools\KeePass.ps1`. It exports from the product
at `a42e38aa`, through `tools\Product.ps1`, the plugin's project and the 207 files under
`src/KeePassGate`, with the root `Directory.Build.props` and `.editorconfig` the project inherits.
It builds that project in Release into `work\plugin\`, against `.keepass\2.62\KeePass.exe`, and
then runs `sizes.ps1` into `out\sizes.txt` and `constants.ps1` into `out\constants.txt`. `work\` and
`out\` are ignored by the branch. The build needs the .NET SDK and the packages the plugin names,
`BouncyCastle.Cryptography` 2.7.0, `Microsoft.OpenApi` and `Microsoft.OpenApi.Readers` 1.6.31,
`Newtonsoft.Json` 13.0.4 and `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3. Nothing shows a
window, and no database is written to a file.

How long it ran here, in two runs, with the machine busy with other measurements: the export about
30 seconds, three `git` calls a file; the build about 4 seconds; `sizes.ps1` 2.3 s and 1.6 s;
`constants.ps1` 0.3 s each time. The report states no time.

**The build is not the recorded run's.** The recorded run loaded a Release build of the plugin
dated 2026-11-02, copied from the product's worktree and older than `a42e38aa`, which no commit
named anywhere rebuilds. The build here is made at `a42e38aa`. Every length below that does not
move from run to run is the same on both, which bears out the report's statement that the stored
formats are the formats the code at `a42e38aa` documents.

**How a result is judged.** As in the other families: "yes" is that this run gives what the report
quotes, "differs" that it gives something else, with the note stating whether that is noise, the
environment or the claim, and "no" that neither the record nor this run gives what the report
states.

## The probe, `sizes.ps1`

It loads KeePass 2.62's `KeePass.exe` and the plugin's assemblies, makes a `PwDatabase` in memory,
creates an authority through the plugin's `AuthorityService` over its `CustomDataStore`, issues one
client named `agent-fictitious` with the profiles `ops` and `reader`, and prints the length of each
stored text the plugin's own writers produce. It takes the directory holding the plugin's build and
the path of `KeePass.exe`. Output: what it prints, `out\sizes.txt`.

All sections below are of `papers/ground/ground-inventory.md`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| I1 | the authority document, `KPG.Authority.<fp>`, is 2903 characters | §1.1 D1; §8 row 1; §1.4 as 2.9 KB | `sizes.txt` line 4 | 2903 | 2903; 2891 in the first run | yes | not in every run: over 54 runs of the same build it was 2903 in 41 and 2891 in 13. The document holds two PKCS#12 files made with fresh keys, serial numbers and signatures, so its encoded length is not fixed. Noise, and the recorded length is the commoner |
| I2 | the client certificate with two profile claims is 487 bytes of DER | §1.1 D3; §8 row 2 | line 5 | 487 | 489; 487 in the first run | differs | noise, for the same reason: over 54 runs 487 in 16, 488 in 25 and 489 in 13. The record that carries it does not move, I3 |
| I3 | an admission record with two profiles is 819 characters | §1.1 D3; §8 row 3; §1.4 | line 6 | 819 | 819, in all 54 runs | yes | 487, 488 and 489 bytes all take 652 characters of base64 |
| I4 | a record naming 32 profiles of 62 characters is 2824 characters | §1.1 D3; §8 row 4 | line 7 | 2824 | 2824 | yes | |
| I5 | a profile list of three names is 38 characters | §1.1 D6; §8 row 5 | line 8 | 38 | 38 | yes | |
| I6 | a profile list of 32 names of 62 characters is 2094 characters | §1.1 D6; §8 row 6 | line 9 | 2094 | 2094 | yes | |
| I7 | a typical two-profile rule is 101 characters and reads | §1.2 N1; §8 row 7; §1.4 as 2.0 KB for twenty | line 10 | `101 chars, reads: True` | the same | yes | |
| I8 | the largest rule the reader accepts, 31 profiles of 64 characters with all eight words and `deny`, is 4482 characters and reads | §1.2 N1; §8 row 8 | line 11 | `4482 chars, reads: True` | the same | yes | the probe shows that this rule reads, not that a larger rule is refused; "largest" rests on the limits the report reads, `Acl\AclDocument.cs:80` and `Transport\JsonBody.cs:58-61` |
| I9 | `sizes.ps1` printed the history defaults empty, "my script reading them wrongly" | §8, below the table | line 2 | `history defaults: items , size`, with nothing after either name | the same | yes | a defect of the probe, left in place: both constants are `internal` in `KeePassLib/PwDatabase.cs:47-48`, and `[Type]::Name` reaches only public members. It changes no quoted number, because the report quotes the line appended below it, I10 |

## The added script, `constants.ps1`

The report quotes the history defaults and the maintenance period from a line appended to
`sizes.txt` "by reflection". The command that appended it was not kept, so `constants.ps1` is
written here to print the same line from KeePass 2.62's KeePassLib, reading the two constants as
non-public static fields and the period off a new `PwDatabase`. It takes the path of `KeePass.exe`.
Output: `out\constants.txt`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| I10 | `DefaultHistoryMaxItems` 10, `DefaultHistoryMaxSize` 6291456, and a new database's `MaintenanceHistoryDays` 365 | §0 item 6 ("10 revisions or 6 MiB"); §4.2; §8 rows 9 and 10 | `constants.txt` line 1; recorded as `sizes.txt` line 14 | `PwDatabase.DefaultHistoryMaxItems = 10, DefaultHistoryMaxSize = 6291456, new PwDatabase MaintenanceHistoryDays = 365 (KeePass 2.62)` | the same line | yes | the second line of `constants.txt` adds that a new database starts with those two limits, `HistoryMaxItems = 10` and `HistoryMaxSize = 6291456`, which `ground-keepass` X5 shows at work |

## Claims no probe here backs

**Presented as measured, and arithmetic.** Section 1 states "Sizes are measured (here) unless
marked", but `sizes.ps1` prints the authority's key cut to 40 characters and no other key at all.
These follow from the formats instead, and were checked here by reading the product at `a42e38aa`
(`git grep` over `AuthorityKeys.cs`, `ProfileNames.cs` and `RenewalRefusal.cs`, read only):

| Claim | Quoted in | What was found |
| --- | --- | --- |
| key lengths: D1 78, D2 85, D3 75, D4 76, D5 83, D6 12 | §1.1 | the prefixes `KPG.Authority.` (14), `KPG.RetiredAuthority.` (21), `KPG.Client.` (11), `KPG.Revoked.` (12) and `KPG.RenewalRefused.` (19) at `Authority\AuthorityKeys.cs:41`, `:54`, `:112`, `:158` and `:243`, each followed by 64 hex digits, and `KPG.Profiles` at `Acl\ProfileNames.cs:45`: the lengths agree |
| values: D2 7, D5 7 or 20, D4 85 "by construction" | §1.1; §0 item 2 ("76 + 85", "85 + 7") | `retired` at `AuthorityKeys.cs:57` and `forever` at `Authority\RenewalRefusal.cs:33` are 7; a moment in RFC 3339 with `Z` is 20; 20, a space and 64 hex digits are 85: they agree |
| some 14 KB of KPG data in the example database | §1.4 | marked as arithmetic on measured sizes, and the sum is 13.9 KB as written. It counts the authority, the retirements and the revocations with their keys and the ten records without theirs (10 times 819 is 8.2 KB; with the 75 character keys, 8.9 KB). With every key the example comes to about 14.7 KB |

**Measured elsewhere, and not run here.** The report also cites:

- the branch `spikes/revocation-merge`, as "measured (spike)": §0 items 2 and 5, P1 to P3 and P11
  in §2, the whole of §3, the KeePassXC and Android rows of §4.1 and §4.4, and §7 item 10. Its
  measurements are digested by `papers/ground/ground-measured.md`;
- the product's host tests at `a42e38aa`, as "measured (host test)": P4, P5, P10 and P16 in §2, and
  the KeePass rows of §4.1, §4.3 (`NodeTagsTests.cs:927-948`, `:968-981`) and
  `RemovalMergeTests.cs`. The report read them and ran none, and no test tier runs here either;
- the product's `FINDINGS.md:414` and `:417`, as "measured" in §4.3, which records measurements
  made on the product's branches.

## What `ground-keepass` measured of what this report assumed

The inventory had to assume several things about KeePass that the `ground-keepass` family's
experiments then ran on KeePass 2.62, in `spikes\storage\probes\ground-keepass\RUN.md`:

- **§4.1, a different field added in each copy of one entry**, "not measured anywhere": X2 measures
  it. The newer entry wins whole and the other copy's field survives only in its history; at a tie,
  at a collision of history times, under Keep existing when the arriving copy is newer, or with a
  history limit of 0, it survives nowhere.
- **§4.4, two entries added to one group, one in each copy**, "assumed both arrive": X1 measures
  it, and both arrive under all five methods in both directions.
- **§4.4, whether deletions travel for entries**, "to measure": X3 and X4 measure it. Only
  Synchronize carries a deletion to the other copy.
- **§4.3 and §9 item 8, the tie rule of `NodeTagsTests` on 2.62**: X2's equal row shows
  Synchronize taking the arriving copy at a tie on 2.62, as the test records for 2.61.1.
- **§4.2, Database Maintenance using `MaintenanceHistoryDays`**, "assumed": read there, in
  `KeePass/Forms/DatabaseOperationsForm.cs:123-157`, and not run, since it needs a window.

## Changes made to run it here

- **`sizes.ps1` is unchanged**, byte for byte. It always took the plugin's directory and
  `KeePass.exe` as parameters, so nothing in it named a path.
- **`run.ps1` and `constants.ps1` are added**: the first to build the plugin and pass `sizes.ps1`
  the paths the recorded run passed by hand, the second for the line nothing kept the command of.
- **The plugin is built rather than carried**, at `a42e38aa`, as above.

**Recorded output.** `recorded\sizes.txt` is byte for byte the file the report quotes, its three
appended lines included. It holds no machine path, user name or session identifier, so nothing was
replaced; the key it prints, cut to 40 characters, is that of an authority made for the run and
thrown away.

## Not carried

- **`bin\`**: the six assemblies of the 2026-11-02 Release build, `KeePassGate.dll` and the five it
  loads. They are build output, and the product's binaries are not this branch's to keep; the
  build at `a42e38aa` takes their place.
