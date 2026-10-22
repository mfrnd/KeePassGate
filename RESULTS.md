# What the probe settled

Whether KeePass 2.61.1 keeps anything that can serve as the version of a group or an entry, and what
a digest of the content costs instead. Every row was measured on .NET Framework 4.8 on the
development machine, against databases the probe made in memory; the output is under
[`spikes/tags/output`](spikes/tags/output) as it came back. [`README.md`](README.md) says how to run
it.

| Question | Answer |
| --- | --- |
| Does KeePass keep a version counter on a node? | **No.** There is a modification time, a time the node last moved, and a usage count that `Touch` raises for an access as well as a change |
| Does the modification time move with every change a route would serve? | **No**, in four ways: two changes inside one clock step, a merge at an equal time, a move, and a plugin that never calls `Touch` |
| Do KeePass's own windows always stamp it? | **Yes**, every edit KeePass makes from its own windows ends in `Touch` or a set of the time |
| Can an attachment's digest be kept per object? | **Yes.** A `ProtectedBinary` offers nothing that changes its bytes, and KeePass keeps the object's own hash code inside it |
| What does a digest of a node cost? | Microseconds for the strings and for a group, and about 2 GB/s over attachment bytes |
| Verdict | **An HMAC of the content, under a key drawn per process.** Times refused, and a plain hash refused |

## 1. The time, `output/times.txt`

| What was measured | What came back |
| --- | --- |
| `DateTime.UtcNow`, which `Touch` stamps, sampled for one second | 200 steps; smallest 0.51 ms, median 1.52 ms, largest 1.62 ms. Windows can step as coarsely as 15.6 ms |
| a thousand pairs of `Touch`, a change between the two of each pair | **all 1000 pairs carried the same time** |
| a KDBX 4 save read back | the time keeps whole seconds; 237 ms were lost on this run, so every time differs after a reload with nothing changed |
| a synchronize where the other copy changed later | its strings are taken, with its time |
| a synchronize where the other copy changed with a time 30 seconds older | nothing is taken, and the time stays |
| a synchronize where the other copy changed at **the very same time** | **its strings are taken and the time does not move** |
| a synchronize where the other copy moved the entry to another group | the entry moves; `LocationChanged` moves and the modification time does not |
| a move inside one database, by `Remove` and `AddEntry` as the main window makes one | the same: only `LocationChanged` moves |

So a tag made of the modification time misses two changes inside one step, a merge at an equal time
and a move, and a tag made of both times still misses the first two. A reload changes every one of
them for nothing anybody wrote.

## 2. KeePass's own writers, `output/survey.txt`

Every method in `KeePass.exe`, KeePassLib included, whose IL calls something that changes what a
route would serve of an entry or a group: a string or an attachment set or removed, an expiry, a
group's name or notes, a node added to a group or removed from one. 182 methods, each marked `touch`
when the same method also calls `Touch`, sets the modification time or takes a history backup, and
`NOTOUCH` otherwise. Per method, so a `NOTOUCH` is a lead to read rather than a verdict: a caller
may stamp what its callee does not.

- **Importers**, under `KeePass.DataExchange`, are 78 of the 151 `NOTOUCH` lines, beside 31 marked
  `touch`. They make new nodes, which have no earlier tag to be wrong about.
- **The entry dialog** sets strings, attachments and the expiry on its own working copy, and
  `PwEntryForm.SaveEntry` stamps the entry when OK commits it. `GroupForm.OnBtnOK` does the same for
  a group.
- **Every other edit from KeePass's own windows** is stamped where it is made: an expiry set from
  the main window, an attachment opened and saved back, the OTP settings, the `{NEWPASSWORD}`
  placeholder, a TAN expired on use, a delete to the recycle bin, a restore from history, and a
  merge's `AssignProperties`.
- **What is left** is moves, which stamp `LocationChanged` only, and code that builds lists,
  clipboards and search results out of copies.

So KeePass's own misses are a move and a merge at an equal time. A plugin that changes a value
without calling `Touch` is nobody's promise.

## 3. An attachment, `output/binary.txt`

`ProtectedBinary` declares five public readers, `IsProtected`, `Length`, `ReadData`,
`ReadXorredData` and `GetHashCode`, beside `Equals`. Its only writers are private: `Init`, from its
constructors, and `Encrypt` and `Decrypt`, which change how the same bytes are held in memory. No
property can be set. And it keeps `m_oiHash`, its own hash code, once asked, which KeePass could not
do of an object whose bytes moved. So a digest kept against one object stays true for as long as the
object lives, and a changed attachment is a new object.

## 4. What a digest costs, `output/cost.txt`

| What was hashed | HMAC-SHA256 |
| --- | --- |
| an entry's ten strings, two kilobytes of notes, protected ones decrypted | 0.0089 ms on this run, 0.006 ms on the first |
| a group's name, 500 bytes of notes and its identifier | 0.0029 ms on this run, 0.006 ms on the first |
| a 1 MB attachment | 0.48 ms, about 2 GB/s |
| a 10 MB attachment | 6.09 ms on this run and 5.00 ms on the first, against 6.84 ms and 8.33 ms to decrypt it when protected |
| a 100 MB attachment | 124 ms on this run and 56 ms on the first; three samples each, so the noisiest row |

The machine's SHA extensions carry all three hash classes .NET Framework offers to the same speed.

## 5. The verdict

**The tag is an HMAC-SHA256 over what the routes serve**: for an entry its identifier, its group,
every string with its name and whether it is protected, every attachment's name and bytes, and its
expiry; for a group its identifier, its parent, its name and its notes. Times stay out, which keeps a
tag across a reload, and so do rules, which no route serves.

**A plain hash was refused before it was written.** A client holding `read` without `protected` sees
every string of an entry but the protected ones, and the tag beside them; over a plain hash it could
try passwords offline until one reproduced the tag. Under a key drawn when the plugin loads and kept
in its memory, nothing outside the process can compute a tag for a guess. What is left is that one
content gives one tag for as long as KeePass runs, and that every tag changes when it restarts, which
costs a refused write and never an overwrite.

**Each attachment is hashed once per object**, kept in a `ConditionalWeakTable` keyed by the object,
on the strength of section 3.
