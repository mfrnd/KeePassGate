# Probes of `rollback-rule-models`

A trial written on this branch for the storage evaluation, not carried from an earlier working
directory: what a rollback of the access rules does under today's rule model and under the
grant-only model with its middle form. A rollback here is a node's rule returning to a state it
really had earlier, after the operator has since changed it. The grant-only design's probe has a
trial it calls a rollback (`grant-only\design\probe\Program.cs` lines 355 to 371), which replaces a
node's document by a random document, a state the node never had; this family measures rollbacks.
The report that quotes it is `papers\ground\measured-rollback-rule-models.md`, and "report 5.3"
below names its section 5.3.

| Directory | What runs | Command, from the branch root |
| --- | --- | --- |
| `rollback-rule-models\` | `probe\Program.cs`, built as `Rollback.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rollback-rule-models\run.ps1` |

What a run needs: Windows PowerShell 5.1, the .NET SDK (the project targets `net48` through
`Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 and takes `Newtonsoft.Json` 13.0.4 from NuGet),
and git with the product's commit in this repository's history, from which `tools\Product.ps1`
exports the product's files. KeePass is not needed: no KeePass type is used, and no database is
made.

## What it measures

**Two models of the rules, one intent.** Today's model is the product's own `AclResolver`: the
project compiles the eight files the grant-only design's probe compiles (`Acl\AclDocument.cs`,
`AclDocuments.cs`, `AclGrant.cs`, `AclInheritance.cs`, `AclResolver.cs`, `AclVerb.cs`,
`VerbMask.cs`, `Transport\JsonBody.cs`), exported from `a42e38aa`. Grant-only and its middle form
are the design probe's `GrantOnly`, `Middle` and fold of records, carried unchanged into
`Program.cs` with the generator they were measured with. The language is the language of both: the
product's resolver is C# for .NET Framework 4.8, and so is the design's probe.

**The trial.** A history of edits of the kinds the operator makes (grant, widen, narrow, an empty
rule, a seal, remove, a rule below an inherited rule, and the grant-only form's own act, a grant
with its own switches below an inherited rule). After each edit the operator's intent is an access
table, the same for every model: 14 cells per node and profile, create, read, update and list each
on the fields, with attachments and with protected values, and strip and delete. Each model
expresses the intent in its own records as its design prescribes: today one document per node;
grant-only rows with the design's push-down, written as grants and withdrawals (design 5.2); the
middle form grants and withheld marks with lifts. Where a model cannot give the intent it gives the
widest table within it. After the history, five kinds of loss are applied to each model's store and
the table after the loss is compared with the last intent:

- (a) the whole store as it was k edits ago, for every k;
- (b) one node's rule back to its state before its last change, the rest as it is; and beside it
  the same loss by the mechanism `RESULTS.md` 2.5 names, a stale copy of the node itself winning by
  time;
- (c) a node's newest records lost, older records and deletions as they are;
- (d) one record of each kind lost: a document, a grant, a withdrawal, a mark with or without its
  reference, a lift;
- (e) two edits of one node made apart, then both kept: today as "what both allow" (`rules-central`
  section 3.5), grant-only as the union of its records; held to what both edits allow and to what
  either allows.

A trial counts as **wider** when the table after the loss grants any cell the intent refuses, with
the number of such cells. Each grant-only form runs twice: withdrawing a row taken away whole with
the letters removed (design 5.2, and scenarios 02 and 15 of the design's probe), and, as "row
whole", with all eight letters (scenario 04 of the design's probe).

**Two worlds.** A sample: 1,000 histories of 20 edits on trees of 30 nodes from the design's
generator, three profiles, seed 20261116. An exhaustive world: every history of 4 edits from an
empty policy on the tree root, G, X and S in G, S1 in S, with one profile and a small alphabet,
116,745 histories.

**Two controls.** Control 1 runs the design's own trial 4, and its trial 2 from the same loop,
unchanged: its generator, seed 20261115 and draws, answered in its 18 answers and in this trial's 14
cells. Control 2 holds each model's table, read from its own store after every edit with no loss, to
the intent.

**Command.** `run.ps1` above. It exports the product's eight files, builds `probe\Rollback.csproj`
into `bin\`, runs `Rollback.exe out\rollback.txt 1000 30 20 20261116 4`, prints the run's duration,
and compares the fresh file with `recorded\rollback.txt` line for line.

**Output.** `out\rollback.txt`; the recorded copy `recorded\rollback.txt`. The file holds no time,
no path, no user name and no session, so nothing was scrubbed, and a fresh run is held to the record
byte for byte.

**How long.** 190.5 s for the run recorded and 193.9 s for the second run, the build about 2 s
besides each, on a machine other work was sharing.

**Compared.** The second run's `out\rollback.txt` equals the record: 413 lines and 413, 0 differ,
and the same SHA-256, `B353468B20BBBFF8161DDD6A937811723CDCF554AD6088348B7B9AF3C9ACF187`. Two
earlier runs, made before three identifiers in `Program.cs` were renamed for the spelling check,
gave the same file in 185.2 s and 198.9 s. A third run, from the branch under a basic user token
when the family was committed, gave the same file again in 119.6 s: 413 lines and 413, 0 differ,
the same SHA-256.

## The figures

Every figure the report quotes, with the line of `recorded\rollback.txt` that holds it. "Sample"
and "exhaustive" are the two worlds; a figure written as trials, wider, cells, mean, most, narrower
only is the order of the record's columns. The second run's value is read off its own file.

| Figure | Quoted in | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| Control 1, a random other document, today's model: trials, wider, answers, narrower; in cells | report 3.1 | 2,802, 2,428, 52,209, 2,440; 41,135 cells (lines 7, 12) | 2,802, 2,428, 52,209, 2,440; 41,135 cells | yes |
| Control 1, a random other document, grant-only, and the unexplained count | report 3.1 | 2,802, 2,280, 55,483, 2,287, unexplained 0; 43,417 cells (lines 8, 13) | the same | yes |
| Control 1, one rule lost, today's model | report 3.1 | 3,175, 1,025, 12,142, 2,834; 9,464 cells (lines 9, 14) | the same | yes |
| Control 1, one rule lost, grant-only | report 3.1 | 3,175, 0, 0, 2,809 (lines 10, 15) | the same | yes |
| Control 2, sample, states equal to the intent, narrower with cells, wider: today, grant-only, middle | report 3.2, 6 | 18,201, 1,799 (4,124), 0; 11,582, 8,418 (77,056), 0; 19,299, 701 (3,191), 0 (lines 35 to 44) | the same | yes |
| Control 2, sample, states each model cannot give exactly; not the intent although it could; store unlike its expression | report 3.2 | 1,799, 8,418, 701; 0; 0 for every model | the same | yes |
| Control 2, exhaustive, the same rows | report 3.2, 6 | 444,635, 22,345 (34,092), 0; 249,500, 217,480 (1,331,426), 0; 439,339, 27,641 (92,178), 0; cannot give 22,345, 217,480, 27,641; the rest 0 (lines 224 to 233) | the same | yes |
| Edits that made an exact state inexact, by kind and model, both worlds | report 4 | lines 46 to 53 and 235 to 242, 48 counts | the same | yes |
| (a), sample: today, grant-only, middle | report 5.1, 6 | 20,000, 15,701, 208,730, 13.3, 420, 4,221; 20,000, 15,474, 196,719, 12.7, 420, 4,443; 20,000, 15,690, 209,129, 13.3, 420, 4,231 (lines 77 to 81) | the same | yes |
| (a), k = 1: sample, exhaustive | report 5.1 | 466, 444, 464 of 1,000; 77,271, 69,356, 76,451 of 116,745 (lines 83, 274) | the same | yes |
| (a), exhaustive | report 5.1, 6 | 466,980, 257,458, 1,737,418, 6.7, 40, 199,615; 247,375, 1,574,580, 6.4, 40, 210,687; 256,854, 1,739,070, 6.8, 40, 199,789 (lines 268 to 272) | the same | yes |
| (b), sample | report 5.2, 6 | 9,922, 5,019, 31,096, 6.2, 406, 4,697; 12,734, 5,950, 37,845, 6.4, 420, 6,748; 9,922, 4,721, 33,285, 7.1, 420, 4,656 (lines 107 to 111) | the same | yes |
| (b), exhaustive | report 5.2, 6 | 344,668, 175,441, 768,132, 4.4, 40, 152,863; 353,772, 180,497, 811,411, 4.5, 40, 164,674; 344,606, 159,578, 865,408, 5.4, 40, 152,713 (lines 282 to 286) | the same | yes |
| (b) by the kind of the last change, sample, trials and wider | report 5.2 | lines 132 to 139, 48 counts, among them narrow 1,435 and 1,435, 1,716 and 1,613, 1,488 and 1,290; grant and widen in the middle form 243 and 143 | the same | yes |
| (b), a stale copy of the node: wider and changed nothing, both worlds | report 5.2, 6 | today as (b) with 300 and 19,350 unchanged; grant-only 0 of 12,734 and 0 of 353,772, all unchanged; middle 0 of 9,922 and 0 of 344,606, all unchanged (lines 116 to 120, 291 to 295) | the same | yes |
| (c), sample | report 5.3, 6 | today as (b); 12,734, 1,376, 3,652, 2.7, 96, 9,946; 9,922, 858, 4,487, 5.2, 96, 5,777 (lines 125 to 129) | the same | yes |
| (c), exhaustive | report 5.3, 6 | today as (b); 353,772, 77,438, 208,026, 2.7, 20, 246,529; 344,606, 31,296, 163,491, 5.2, 20, 236,095 (lines 300 to 304) | the same | yes |
| (c) by kind, sample: grant-only narrow, add below, the rest; middle narrow | report 5.3 | 1,107, 240, 29; 858 (lines 142 to 149) | the same | yes |
| (d), sample: document; grant-only grant, withdrawal; middle grant, withdrawal, mark and reference, mark with reference kept, lift | report 5.4, 6 | 8,525, 1,917, 10,484, 5.5, 208, 5,534; 11,915, 0; 10,897, 2,246, 6,160, 2.7, 116, 6,226; 7,834, 0; 6,274, 1,350, 6,764, 5.0, 116, 356; 2,353, 2,282, 11,211, 4.9, 204, 5; 2,353, 0; 1,397, 0 (lines 154 to 168) | the same | yes |
| (d), exhaustive, the same rows | report 5.4, 6 | 327,734, 125,362, 530,785, 4.2, 32, 175,067; 283,348, 0; 265,793, 103,186, 308,188, 3.0, 20, 123,515; 289,668, 0; 91,454, 35,878, 186,158, 5.2, 20, 4,145; 143,306, 140,975, 590,495, 4.2, 32, 225; 143,306, 0; 45,277, 0 (lines 329 to 343) | the same | yes |
| (e) against what both allow, sample: today, grant-only, middle, the two "row whole" forms | report 5.5, 6 | 4,055, 19, 136, 7.2, 20, 1,214; 2,761, 23,447, 8.5, 416, 888; 2,716, 24,061, 8.9, 474, 167; 2,434, 22,498, 9.2, 416, 1,136; 2,501, 23,277, 9.3, 474, 180 (lines 173 to 177) | the same | yes |
| (e) against what both allow, exhaustive | report 5.5, 6 | 526,850, 1,374, 3,522, 2.6, 12, 54,100; 266,360, 966,282, 3.6, 30, 172,088; 177,232, 582,912, 3.3, 30, 66,414; 229,014, 889,284, 3.9, 30, 201,716; 161,328, 550,602, 3.4, 30, 67,494 (lines 348 to 352) | the same | yes |
| (e) against what either allows, both worlds | report 5.5, 6 | sample 0; 106, 277, 2.6, 34; 120, 361, 3.0, 54; exhaustive 0; 29,078, 45,898, 1.6, 12; 29,348, 55,330, 1.9, 12; "row whole" the same (lines 182 to 186, 357 to 361) | the same | yes |
| (e) with no adds decision in the three policies: trials, wider than both allow, wider than either allows | report 5.5 | sample 1,841: 0, 1,180, 1,171, 1,033, 1,036; either 0, 32, 29; exhaustive 431,716: 0, 200,392, 120,868, 170,080, 106,736; either 0, 15,880, 10,438 (lines 191 to 204, 366 to 379) | the same | yes |
| The examples the report quotes | report 4, 5.5 | lines 248, 255, 260, 405 and 410 | the same | yes |

## Changes made to the probe, and why

None. The probe, its project and `run.ps1` were written on this branch for this measurement; the
parts carried from the design's probe are marked in `Program.cs` with that file's line numbers, and
the field added to its node class, `Index`, is named there.

## What the trial does not cover

- How often each loss happens to each store: the trial measures what a loss does once it has
  happened, and `RESULTS.md` sections 2 to 4 hold which programs cause which loss.
- The tree: no node is moved, copied, displaced, deleted or added, so a narrowing kept by where a
  node sits, which grant-only relies on, is not exercised.
- An intent in which a grant means "add to everything under it": an edit in today's vocabulary is
  read as an exact decision.
- The middle form with push-down, and a mark for every profile at once.
- Authentication of grants and lifts, the withdrawal's two witnesses taken apart, the recycle bin,
  the profile list, clients holding several profiles, and the API's listings.
- Three or more copies, repeated losses, histories longer than 20 edits, and any exhaustive world
  larger than five nodes with one profile.
- Today's model in the product's own store under (c): the product keeps no older version, so the
  trial reads (c) for today's model as the versioned store of `rules-central`, without its refusal
  of a deleted version.
- The plugin, KeePass, KeePassXC and every phone program.
