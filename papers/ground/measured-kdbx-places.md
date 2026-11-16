# Every place in a KDBX 4 file, and what each program does to it

Where a plugin could keep information inside a `.kdbx` beyond the places `RESULTS.md` measured, and
what KeePass 2.62 and 2.47, KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, kdbxweb 2.1.1 and pykeepass
4.2.0 do to each place when they open and save a file, when they merge two copies, and, for KeePass
2.62, when the master key, the key derivation, the file name or the format version changes. The
phone programs are not measured here; section 6 lists what their measurement needs.

Everything in a table was measured by the family `kdbx-places` on the branch
`spikes/storage-survival` (`spikes\storage\probes\kdbx-places`, whose `RUN.md` gives the commands,
the runtime and a second run). Every file was read back by a reader of the file's own bytes, both
headers and the decrypted XML, so the result shows what a program wrote whatever its own reader made
of the file; where the source shows that the loss happened while reading, the prose states it. What
was read from source and not run is marked **read**.

## 1. The inventory

Sources: the KDBX 4.1 specification (`https://keepass.info/help/kb/kdbx.html`) and its 4.1 change
page (`https://keepass.info/help/kb/kdbx_4.1.html`), KeePassLib 2.62 (`KdbxFile.cs`,
`KdbxFile.Read.cs`, `KdbxFile.Read.Streamed.cs`, `KdbxFile.Write.cs`, `PwDatabase.cs`, `PwGroup.cs`,
`PwEntry.cs`, `VariantDictionary.cs`, `KdfParameters.cs`), and for the other programs their sources
as section 5 cites them.

**Protection.** The outer header is authenticated and not encrypted: its SHA-256 and an HMAC over it
follow it in the file. The inner header and the XML are encrypted and authenticated, block by block.
Bytes after the last block are neither: no hash or HMAC covers them (read, every reader cited;
measured: every program here opened such a file).

**Columns.** "Type and limits" is what the format and KeePassLib 2.62 allow (read); a limit another
program sets is in section 5. "In `RESULTS.md`" names the section that already holds a measurement
of the place. "Here" names what this family measured of it: S for saves by every program, M for
merges, K for the key and format changes of KeePass 2.62, C for capacity.

The places in file order: the outer header (`H.*`, authenticated and not encrypted), the inner
header (`I.*`), `Meta` (`M.*`), a group (`G.*`), an entry (`E.*`), the deletion records (`D.*`),
what the format does not define (`X.*`, `XA.*`, `XC.*`), and the bytes after the last block (`T.*`).

| Place | Field or element | Type and limits | In `RESULTS.md` | Here |
| --- | --- | --- | --- | --- |
| `H.version` | the version word | 4.0 or 4.1; KeePassLib writes 4.1 only for a 4.1 feature | 2.1 (which feature needs 4.1), 2.2, 2.3 | S M K |
| `H.comment` | field 1, comment | bytes; "used in earlier versions" of the format; KeePassLib reads it as an unknown field and never writes it | nothing | S M |
| `H.cipher`, `H.compression` | fields 2 and 3 | a cipher UUID; 0 or 1 | nothing; not a carrier | rewritten only by the user |
| `H.seed`, `H.iv` | fields 4 and 7 | 32 bytes; 16 or 12 bytes; fresh at every save (read, every writer) | nothing; not a carrier | S |
| `H.kdf` | field 11, the KDF parameters, a variant dictionary | `$UUID`, `S` (fresh at every save), `R` or `V`, `I`, `M`, `P`; KeePassLib keeps any further key of a known type; a key of an unknown type refuses the file | nothing | S M K |
| `H.pcd` | field 12, public custom data, a variant dictionary | keys of type UInt32, UInt64, Bool, Int32, Int64, String, Byte[]; Int32 sizes, so about 2 GB; "only if it must be readable without decryption", keys named `PluginName_ItemName` | nothing | S M K C |
| `H.eoh` | field 0, end of header | must be `0D 0A 0D 0A`; KeePassLib reads its content and ignores it | nothing | S |
| `H.field13` | a field of an identifier the format does not define | any; KeePassLib skips it with a warning | nothing | S |
| `I.stream` | fields 1 and 2, inner stream algorithm and key | ChaCha20 and a 64-byte key, fresh at every save | nothing; not a carrier | S |
| `I.binary` | field 3, an attachment's bytes and its protection flag | a flag byte and the bytes | 2.1 to 2.4 (attachments) | S M |
| `I.unref` | field 3 that no entry or history item refers to | any bytes | nothing | S M |
| `I.field4` | a field of an identifier the format does not define | any; KeePassLib skips it | nothing | S |
| `M.generator` | `Generator` | text; KeePassLib ignores it on reading and writes `PwDatabase.LocalizedAppName` | nothing | S M |
| `M.settingschanged` | `SettingsChanged` | time, 4.x | 2.3 (kept by two phone programs, not written by KeePassDroid) | S M |
| `M.name`, `M.namechanged` | `DatabaseName`, `DatabaseNameChanged` | text; time | nothing | S M C |
| `M.desc`, `M.descchanged` | `DatabaseDescription`, `DatabaseDescriptionChanged` | text; time | nothing | S M C |
| `M.user`, `M.userchanged` | `DefaultUserName`, `DefaultUserNameChanged` | text; time | nothing | S M C |
| `M.histdays` | `MaintenanceHistoryDays` | unsigned integer | nothing | S M |
| `M.color` | `Color` | `#RRGGBB` or empty | nothing | S M |
| `M.mkchanged` | `MasterKeyChanged` | time | nothing | S M K |
| `M.mkrec`, `M.mkforce` | `MasterKeyChangeRec`, `MasterKeyChangeForce` | 64-bit integer, -1 for off | nothing | S M |
| `M.mkforceonce` | `MasterKeyChangeForceOnce` | Boolean, written only when true | nothing | S M |
| `M.memprot` | `MemoryProtection`, five Booleans | KeePassLib resets them to its defaults at every load | 2.1 (a standard field's protection follows it) | S M |
| `M.icon.data` | `CustomIcons/Icon/Data` with `UUID` | PNG bytes in Base64 | 2.1 X7 (a name or time needs 4.1) | S M C |
| `M.icon.name`, `M.icon.time` | `Icon/Name`, `Icon/LastModificationTime` | text and time, 4.1 | 2.1 X7 | S M C |
| `M.icon.unused` | an icon no node uses | as above | nothing | S M |
| `M.rbenabled`, `M.rbuuid`, `M.rbchanged` | `RecycleBinEnabled`, `RecycleBinUUID`, `RecycleBinChanged` | Boolean; a group's UUID; time | nothing (the bin itself in 2.1 to 2.3) | S M |
| `M.tpl`, `M.tplchanged` | `EntryTemplatesGroup`, `EntryTemplatesGroupChanged` | a group's UUID; time | nothing | S M |
| `M.histmaxitems`, `M.histmaxsize` | `HistoryMaxItems`, `HistoryMaxSize` | 32-bit and 64-bit integers | 2.1 X5 (the open copy's limit applies) | S M |
| `M.lastselected`, `M.lasttopvisible` | `LastSelectedGroup`, `LastTopVisibleGroup` | a group's UUID | nothing | S M |
| `M.cd`, `M.cd.time` | `CustomData/Item` with `Key`, `Value` and, in 4.1, `LastModificationTime` | text; time | 2.1, 2.2, 2.3, 2.4, 2.5 | S M K C |
| `M.binaries` | `Binaries` | KDBX 3.1 only; KeePassLib reads it from a 4.x file too and does not write it there | nothing; not a carrier in 4.x | none |
| `M.headerhash` | `HeaderHash` | KDBX 3.1; KeePassLib checks it when present in a 4.x file too, so a wrong value refuses the file | nothing; not a carrier | none |
| `G.name`, `G.notes` | `Name`, `Notes` | text | 2.1 X6 (notes), 2.2, 2.3 | S M C |
| `G.icon`, `G.customicon` | `IconID`, `CustomIconUUID` | integer; UUID | nothing | S M |
| `G.times.*` | `Times`: `CreationTime`, `LastModificationTime`, `LastAccessTime`, `ExpiryTime`, `Expires`, `UsageCount`, `LocationChanged` | times; Boolean; 64-bit unsigned | modification and location times throughout 2 and 3 | S M |
| `G.expanded` | `IsExpanded` | Boolean | 2.1 X6 (never merged) | S M |
| `G.atseq`, `G.enableautotype`, `G.enablesearching` | `DefaultAutoTypeSequence`, `EnableAutoType`, `EnableSearching` | text; `True`, `False` or `Null` | 2.2 (KeePassXC's group flags) | S M C |
| `G.lasttopentry` | `LastTopVisibleEntry` | an entry's UUID | nothing | S M |
| `G.prevparent` | `PreviousParentGroup` | UUID, 4.1, and dropped by KeePassLib from a 4.0 file | 2.1 X7, 2.2, 2.3 | S M |
| `G.tags` | `Tags` | text, 4.1 | 2.1 X6, X7, 2.2, 2.3 | S M |
| `G.cd` | `CustomData/Item` | text | 2.1 X6, 2.2, 2.3 | S M C |
| `G.cd.time` | a `LastModificationTime` in a group's item | not defined by the format; KeePassLib skips it | nothing | S M |
| `E.icon`, `E.customicon` | `IconID`, `CustomIconUUID` | integer; UUID | nothing | S M |
| `E.fg`, `E.bg` | `ForegroundColor`, `BackgroundColor` | `#RRGGBB` or empty | nothing | S M |
| `E.overrideurl` | `OverrideURL` | text | nothing | S M C |
| `E.qualitycheck` | `QualityCheck` | Boolean, 4.1, written only when false | 2.1 X7, 2.2 (the exclusion from reports) | S M |
| `E.tags` | `Tags` | text | 2.1 X2, 2.2, 2.3 | S M C |
| `E.prevparent` | `PreviousParentGroup` | UUID, 4.1 | 2.1 X7, 2.2, 2.3 | S M |
| `E.times.*` | `Times`, the same seven as a group | as a group | modification and location times throughout | S M |
| `E.title`, `E.string`, `E.protected` | `String` with `Key`, `Value` and `Protected` | text | 2.1 to 2.5 | S M |
| `E.binary` | `Binary` with `Key` and `Value Ref` | a name and a pool index | 2.1 to 2.4 | S M |
| `E.at.*` | `AutoType`: `Enabled`, `DataTransferObfuscation`, `DefaultSequence`, `Association` with `Window` and `KeystrokeSequence` | Boolean; integer; text | nothing | S M C |
| `E.history` | `History/Entry` | whole entries | 2.1 X5, 2.2, 2.3 | S M |
| `E.cd` | `CustomData/Item` | text | 2.1 X2, 2.2, 2.3, 2.4 | S M C |
| `E.cd.time` | a `LastModificationTime` in an entry's item | not defined by the format; KeePassLib skips it | nothing | S M |
| `D.shared`, `D.only` | `DeletedObjects/DeletedObject` with `UUID` and `DeletionTime` | UUID and time; a record that names nothing is allowed | 2.1 X3, X4, 2.2, 2.3, 2.5 | S M |
| `X.*` | an element the format does not define, at 18 positions: in `KeePassFile`, `Meta`, `MemoryProtection`, a custom icon, a `Meta` custom data item, `Root`, a group, its `Times` and custom data item, an entry, its `Times`, `String`, `Binary`, `AutoType`, `Association`, custom data item and history item, and a `DeletedObject` | any; KeePassLib skips it on reading | 2.2 (8 positions, KeePassXC), 2.4 (3 elements, kdbxweb, pykeepass and KeePass 2.62) | S M |
| `XA.*` | an attribute the format does not define, on `Meta`, a `Group`, an `Entry` and a `Value` | any | nothing | S M |
| `XC.meta` | an XML comment in `Meta` | any text | nothing | S M |
| `T.trailing` | bytes after the last block | any; covered by no hash or HMAC | nothing | S M |

Counted element by element and position by position, the seven times of a group and of an entry, the
four parts of `AutoType` and the 18 positions of an unknown element each counted once, these tables
hold **117 places**. 41 of them have a measurement in `RESULTS.md`, several of those only in part
(which format a feature needs, or one program); **76 have none**. This family measured 113 of the
117: all but the cipher, the compression flag, `Binaries` and `HeaderHash`.

## 2. What a save does to each place

Each program opened the base of every set and saved it: KeePass 2.62 and 2.47 as they are (resave)
and after adding an unrelated entry (edit), `keepassxc-cli add` of each release, which is the save
an unrelated edit makes since the command line has no plain resave, and kdbxweb and pykeepass both
ways. A cell holds what the save did on the Argon2d 4.1 base; a set that gave something else is
named in brackets. Where resave and edit differ, the cell names both; they differed nowhere. The
places written by `inject` are in every base, so a program that refuses any of them would have
refused the whole base; none did.

| Place | KeePass 2.62 | KeePass 2.47 | KeePassXC 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 | kdbxweb | pykeepass |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `H.version` | kept | rewritten (to 4.0) [Argon2d 4.0: kept; AES-KDF 4.0: kept] | rewritten (to 4.0) [Argon2d 4.0: kept; AES-KDF 4.0: kept] | rewritten (to 4.0) [Argon2d 4.0: kept; AES-KDF 4.0: kept] | kept | kept | kept | kept |
| `H.comment` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept |
| `H.kdf` | kept | kept | dropped | dropped | dropped | dropped | kept | kept |
| `H.kdf.only` | - | - | - | - | - | - | - | - |
| `H.pcd` | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.uint64` | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.bytes` | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.only` | - | - | - | - | - | - | - | - |
| `I.unreferenced` | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `I.binary` | kept | kept | rewritten (marked protected) | rewritten (marked protected) | rewritten (marked protected) | rewritten (marked protected) | kept | kept |
| `T.trailing` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `M.generator` | rewritten (to empty) | rewritten (to empty) | kept | kept | kept | kept | rewritten (to KdbxWeb) | kept |
| `M.settings-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.name` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.name-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.desc` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.desc-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.user` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.user-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-days` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.color` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-rec` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-force` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-force-once` | kept | kept | dropped | dropped | dropped | dropped | dropped | kept |
| `M.memprot` | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | kept | kept | kept | kept | kept | kept |
| `M.icon.data` | kept | kept | rewritten (to <107 bytes sha256:16e5493c45bc3b75>) | rewritten (to <344 bytes sha256:5d405497ccb5f0d0>) | kept | kept | kept | kept |
| `M.icon.name` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `M.icon.time` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `M.icon.unused` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.icon.only` | - | - | - | - | - | - | - | - |
| `M.bin-enabled` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.bin-uuid` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.bin-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.template` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.template-changed` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-max-items` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-max-size` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.last-selected` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.last-top-visible` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.cd` | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.cd.time` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `M.cd.only` | - | - | - | - | - | - | - | - |
| `G.name` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.notes` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.icon` | kept | kept | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | kept | kept |
| `G.custom-icon` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.creation` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.lastmod` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.last-access` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.expiry` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.expires` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.usage` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.location` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.expanded` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.at-sequence` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.enable-auto-type` | kept | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to true) | kept | kept |
| `G.enable-searching` | kept | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to false) | kept | kept |
| `G.last-top-entry` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.previous-parent` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `G.tags` | kept | dropped | dropped | dropped | kept | kept | rewritten (to kpg, G.tags, base) | kept |
| `G.cd` | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.cd.time` | dropped | dropped | dropped | dropped | dropped | dropped | kept [Argon2d 4.0: dropped; AES-KDF 4.0: dropped] | kept |
| `G.cd.only` | - | - | - | - | - | - | - | - |
| `E.icon` | kept | kept | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | kept | kept |
| `E.custom-icon` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.fg` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.bg` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.override-url` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.quality-check` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `E.tags` | kept | rewritten (to kpg;E.tags;base) | kept | kept | kept | kept | rewritten (to kpg, E.tags, base) | kept |
| `E.previous-parent` | kept | dropped | dropped | dropped | kept | kept | kept | kept |
| `E.times.creation` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.lastmod` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.last-access` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.expiry` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.expires` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.usage` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.location` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.title` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.string` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.protected` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.protected.flag` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.binary` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.enabled` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.obfuscation` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.sequence` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.association` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.history` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.cd` | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.cd.time` | dropped | dropped | dropped | dropped | dropped | dropped | kept [Argon2d 4.0: dropped; AES-KDF 4.0: dropped] | kept |
| `E.cd.only` | - | - | - | - | - | - | - | - |
| `D.shared` | kept | kept | kept | kept | kept | kept | kept | kept |
| `D.only` | - | - | - | - | - | - | - | - |
| `XC.meta` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept |
| `X.*` (18 positions) | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept |
| `XA.*` (4 positions) | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept |

Three places are in files of their own, each the base with one more thing that a reader of the
format may not expect. A refusal here is the program's `load` failing, with the error the log of the
run holds: kdbxweb's `TypeError: Cannot read properties of undefined (reading 'name')` and
pykeepass's `MappingError` naming the identifier.

| File | Place | KeePass 2.62 | KeePass 2.47 | KeePassXC 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 | kdbxweb | pykeepass |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| a41 risky-hfield | `H.field13` | dropped | dropped | dropped | dropped | dropped | dropped | refused | refused |
| a41 risky-ifield | `I.field4` | dropped | dropped | dropped | dropped | dropped | dropped | refused | refused |
| a41 risky-eoh | `H.eoh` | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 00d0ad0a) | kept |
| e41 risky-hfield | `H.field13` | dropped | dropped | dropped | dropped | dropped | dropped | refused | refused |
| e41 risky-ifield | `I.field4` | dropped | dropped | dropped | dropped | dropped | dropped | refused | refused |
| e41 risky-eoh | `H.eoh` | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 00d0ad0a) | kept |

**What the tables show beside the cells.**

- **Fresh at every save, in every program**: the master seed, the encryption IV, the KDF salt `S`
  and the inner stream key. No program kept any of them.
- **The order of the outer header fields** is written again by every program but pykeepass: KeePass
  puts the KDF parameters before the IV, KeePassXC and kdbxweb after it, and pykeepass writes the
  order it read.
- **kdbxweb writes the end of header field as the four bytes `00 D0 AD 0A`**, in the outer and in
  the inner header, where the format names `0D 0A 0D 0A` and an empty field. KeePassLib and
  KeePassXC ignore the content of that field (read); no other program here was given a file kdbxweb
  wrote.
- **A tag holding a colon is split.** KeePass 2.47 and kdbxweb take `:` as a separator in the
  entry's tags, and write the marker `kpg:E.tags:base` back as `kpg;E.tags;base` and `kpg, E.tags,
  base`; KeePass 2.62 and KeePassXC kept it.
- **What the programs add.** Every KeePassXC release adds `_LAST_MODIFIED` to the custom data of the
  database, of the group and of the entry; 2.7.12 and 2.8.0 add `KPXC_RANDOM_SLUG` to the
  database's.
- **`Generator`** is written empty by KeePassLib used as a library, both releases (it writes
  `PwDatabase.LocalizedAppName`, which only KeePass's window sets, read), `KdbxWeb` by kdbxweb, and
  kept as read by KeePassXC and pykeepass.
- **A group's `EnableAutoType` and `EnableSearching`** are written by KeePass 2.47 and every
  KeePassXC release in lower case: `True` comes back as `true`, which every reader takes as the same
  value.

## 3. What a merge does to each place

Each cell sums up four merge runs of the Argon2d 4.1 set: the pair whose times differ (A older than
B) with A open and B arriving, and the other way round, then the pair whose times are equal, both
ways. "The later time wins" is B's value in both runs of the first pair; "the open copy's stays" and
"the arriving copy's replaces" are the same copy's role in both runs, whatever the times; the
bracket after "the later time wins" names the copy whose value the result held at equal times. A
place a program's own save drops is "dropped" here too. KeePass's five methods also ran on the
AES-KDF and the two 4.0 sets, and a set that gave something else is named in brackets; KeePassXC's
four releases also ran with the slow key derivation, where the two loads of a merge fall in
different seconds; kdbxweb also ran on the AES-KDF set. Which time decides is a place's own time
where the format defines such a time (the name's `DatabaseNameChanged`, a custom data key's or an
icon's `LastModificationTime`), the node's `LastModificationTime` for what a group or an entry
holds, and `SettingsChanged` for `Color` and the public custom data (read, section 5).

| Place | Synchronize | Overwrite if newer | Overwrite existing | Keep existing | Create new IDs | KeePassXC 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 | kdbxweb |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `H.version` | kept | kept | kept | kept | kept | rewritten (to 4.0) | rewritten (to 4.0) | kept | kept | kept |
| `H.comment` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `H.kdf` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | dropped | dropped | dropped | dropped | the open copy's stays |
| `H.kdf.only` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | dropped | dropped | dropped | dropped | the open copy's stays |
| `H.pcd` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `H.pcd.uint64` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `H.pcd.bytes` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `H.pcd.only` | united | united | united | the open copy's stays | united | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `I.unreferenced` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `I.binary` | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again | with the entry (`E.binary`), the pool written again |
| `T.trailing` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `M.generator` | rewritten (to empty) | rewritten (to empty) | rewritten (to empty) | rewritten (to empty) | rewritten (to empty) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | rewritten (to KdbxWeb) |
| `M.settings-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.name` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.name-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.desc` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.desc-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.user` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.user-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.history-days` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.color` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.key-changed` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.key-rec` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.key-force` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.key-force-once` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | dropped | dropped | dropped | dropped | dropped |
| `M.memprot` | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `M.icon.data` | the later time wins (equal times: the open copy's) [Argon2d 4.0: the open copy's stays; AES-KDF 4.0: the open copy's stays] | the later time wins (equal times: the open copy's) [Argon2d 4.0: the open copy's stays; AES-KDF 4.0: the open copy's stays] | the later time wins (equal times: the open copy's) [Argon2d 4.0: the open copy's stays; AES-KDF 4.0: the open copy's stays] | the later time wins (equal times: the open copy's) [Argon2d 4.0: the open copy's stays; AES-KDF 4.0: the open copy's stays] | the later time wins (equal times: the open copy's) [Argon2d 4.0: the open copy's stays; AES-KDF 4.0: the open copy's stays] | rewritten (decoded and encoded again as PNG) | rewritten (decoded and encoded again as PNG) | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.icon.name` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | dropped | dropped | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.icon.time` | the later time wins | the later time wins | the later time wins | the later time wins | the later time wins | dropped | dropped | the open copy's stays | the open copy's stays | the later time wins |
| `M.icon.unused` | kept | kept | kept | kept | kept | kept | kept | kept | kept | dropped |
| `M.icon.only` | united | united | united | united | united | united | united | united | united | dropped |
| `M.bin-enabled` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.bin-uuid` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.bin-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.template` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `M.template-changed` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `M.history-max-items` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.history-max-size` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the arriving copy's replaces |
| `M.last-selected` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `M.last-top-visible` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `M.cd` | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the arriving copy's replaces | the open copy's stays | the later time wins (equal times: the open copy's) | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | the later time wins (equal times: the open copy's) |
| `M.cd.time` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the later time wins | dropped | dropped | the open copy's stays [slow KDF: A older and open: rewritten (to 2026-11-16T13:01:58Z); B newer an...] | the open copy's stays [slow KDF: A older and open: rewritten (to 2026-11-16T13:07:28Z); B newer an...] | the later time wins |
| `M.cd.only` | united | united | united | the open copy's stays | united | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | the open copy's stays [slow KDF: the arriving copy's replaces] | united |
| `G.name` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `G.notes` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `G.icon` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | the later time wins (equal times: the open copy's) |
| `G.custom-icon` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.creation` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.lastmod` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins | the later time wins | the later time wins | the later time wins | the later time wins |
| `G.times.last-access` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the open copy's stays | rewritten (to the time of the merge) where the arriving copy is newer, else the open copy's stays | rewritten (to the time of the merge) where the arriving copy is newer, else the open copy's stays | rewritten (to the time of the merge) where the arriving copy is newer, else the open copy's stays | rewritten (to the time of the merge) where the arriving copy is newer, else the open copy's stays | the later time wins |
| `G.times.expiry` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `G.times.expires` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.usage` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `G.times.location` | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins |
| `G.expanded` | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `G.at-sequence` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) |
| `G.enable-auto-type` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the later time wins (equal times: the open copy's; `Null` written as `null`) |
| `G.enable-searching` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the open copy's stays (written in lower case) | the later time wins (equal times: the open copy's; `Null` written as `null`) |
| `G.last-top-entry` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | rewritten (to the zero UUID) in three of the four runs | rewritten (to the zero UUID) in three of the four runs | rewritten (to the zero UUID) in three of the four runs | rewritten (to the zero UUID) in three of the four runs | the later time wins (equal times: the open copy's) |
| `G.previous-parent` | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | dropped | dropped | the open copy's stays | the open copy's stays | the open copy's stays |
| `G.tags` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | dropped | dropped | the open copy's stays | the open copy's stays | the open copy's stays, rewritten (joined by `, `) |
| `G.cd` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `G.cd.time` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | - |
| `G.cd.only` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `E.icon` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | the later time wins (equal times: the open copy's) |
| `E.custom-icon` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.fg` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.bg` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.override-url` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.quality-check` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | dropped | dropped | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the open copy's stays |
| `E.tags` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's), rewritten (joined by `, `) |
| `E.previous-parent` | the later time wins (equal times: the open copy's) | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | dropped | dropped | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the open copy's stays |
| `E.times.creation` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.lastmod` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins | the later time wins | the later time wins | the later time wins | the later time wins |
| `E.times.last-access` | the later time wins | the later time wins | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins | the later time wins | the later time wins | the later time wins | the later time wins |
| `E.times.expiry` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.times.expires` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.usage` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.times.location` | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the later time wins | the later time wins | the later time wins | the later time wins | the later time wins |
| `E.title` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.string` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.protected` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.protected.flag` | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.binary` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.at.enabled` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.at.obfuscation` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.at.sequence` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.at.association` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) |
| `E.history` | a history item added (none at equal times) | a history item added (none at equal times) | a history item added | a history item added (none at equal times) | kept | a history item added (none at equal times) | a history item added (none at equal times) | a history item added (none at equal times) | a history item added (none at equal times) | a history item added (none at equal times) |
| `E.cd` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the open copy's stays |
| `E.cd.time` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | - |
| `E.cd.only` | the later time wins (equal times: the arriving copy's) | the later time wins (equal times: the arriving copy's) | the arriving copy's replaces | the open copy's stays | the open copy's stays | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the later time wins (equal times: the open copy's) | the open copy's stays |
| `D.shared` | the later time wins | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays |
| `D.only` | united | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | the open copy's stays | united |
| `XC.meta` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.*` (18 positions) | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XA.*` (4 positions) | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |

**Beside the cells.**

- **kdbxweb's merge deletes every custom icon no node uses, and writes a deletion record for every
  such icon.** The base's unused icon and the icon only A or only B held came out of every kdbxweb
  merge as deletion records with the time of the merge (`merges\kdbxweb\a41\diff-AB.txt`). A KeePass
  Synchronize applies such a record to an icon whose own time is older (read, `MergeInCustomIcons`),
  so the record goes on to remove the icon from a copy that never passed through kdbxweb; that
  sequence was not run.
- **KeePassXC's merge of the database's custom data depends on the seconds.** With the fast key
  derivation both loads fall in the same second and nothing of it is merged; with the slow key
  derivation, in all four releases, the arriving copy's dictionary replaced the open copy's whole,
  keys only the open copy held included, as `RESULTS.md` section 2.2 has it, and 2.7.12 and 2.8.0
  wrote the time of the load as each key's time. Every other place gave the same cell with the slow
  key derivation as with the fast, but for the time of the merge KeePassXC writes into a group's
  `LastAccessTime`.
- **No KeePass method leaves `Meta` alone.** Under Keep existing, which merges no other database
  property, a custom icon still takes the arriving copy's newer name, time and data, and an icon
  only the arriving copy holds is added.
- **The public custom data merges like nothing else in the header.** Every KeePass method but Keep
  existing adds every key only the arriving copy holds; for a key both hold, Synchronize, Overwrite
  if newer and Create new IDs take the value of the copy whose `SettingsChanged` is later, keeping
  the open copy's at equal times, and Overwrite existing takes the arriving copy's. KeePassXC and
  kdbxweb never merge it.

## 4. Key and format changes, capacity, and who sees each place

### 4.1 KeePass 2.62: a new master key, another key derivation, Save As, and 4.1 brought down to 4.0

Each operation ran once on the Argon2d and once on the AES-KDF 4.1 base. "Down to 4.0" removes every
feature that makes the file 4.1 (group tags, a quality check turned off, an icon's name and time,
the custom data keys' times) and saves.

| Place | New master key | Other KDF | Save As | Down to 4.0 |
| --- | --- | --- | --- | --- |
| `H.version` | kept | kept | kept | rewritten (to 4.0) |
| `H.comment` | dropped | dropped | dropped | dropped |
| `H.kdf` | kept | kept | kept | kept |
| `H.kdf.only` | - | - | - | - |
| `H.pcd` | kept | kept | kept | kept |
| `H.pcd.uint64` | kept | kept | kept | kept |
| `H.pcd.bytes` | kept | kept | kept | kept |
| `H.pcd.only` | - | - | - | - |
| `I.unreferenced` | dropped | dropped | dropped | dropped |
| `I.binary` | kept | kept | kept | kept |
| `T.trailing` | dropped | dropped | dropped | dropped |
| `M.generator` | rewritten (to empty) | rewritten (to empty) | rewritten (to empty) | rewritten (to empty) |
| `M.settings-changed` | kept | kept | kept | kept |
| `M.name` | kept | kept | kept | kept |
| `M.name-changed` | kept | kept | kept | kept |
| `M.desc` | kept | kept | kept | kept |
| `M.desc-changed` | kept | kept | kept | kept |
| `M.user` | kept | kept | kept | kept |
| `M.user-changed` | kept | kept | kept | kept |
| `M.history-days` | kept | kept | kept | kept |
| `M.color` | kept | kept | kept | kept |
| `M.key-changed` | rewritten (to 2026-11-16T13:10:03Z) [AES-KDF 4.1: rewritten (to 2026-11-16T13:10:05Z)] | kept | kept | kept |
| `M.key-rec` | kept | kept | kept | kept |
| `M.key-force` | kept | kept | kept | kept |
| `M.key-force-once` | kept | kept | kept | kept |
| `M.memprot` | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) | rewritten (to KeePassLib's defaults, at load) |
| `M.icon.data` | kept | kept | kept | kept |
| `M.icon.name` | kept | kept | kept | dropped |
| `M.icon.time` | kept | kept | kept | dropped |
| `M.icon.unused` | kept | kept | kept | kept |
| `M.icon.only` | - | - | - | - |
| `M.bin-enabled` | kept | kept | kept | kept |
| `M.bin-uuid` | kept | kept | kept | kept |
| `M.bin-changed` | kept | kept | kept | kept |
| `M.template` | kept | kept | kept | kept |
| `M.template-changed` | kept | kept | kept | kept |
| `M.history-max-items` | kept | kept | kept | kept |
| `M.history-max-size` | kept | kept | kept | kept |
| `M.last-selected` | kept | kept | kept | kept |
| `M.last-top-visible` | kept | kept | kept | kept |
| `M.cd` | kept | kept | kept | kept |
| `M.cd.time` | kept | kept | kept | dropped |
| `M.cd.only` | - | - | - | - |
| `G.name` | kept | kept | kept | kept |
| `G.notes` | kept | kept | kept | kept |
| `G.icon` | kept | kept | kept | kept |
| `G.custom-icon` | kept | kept | kept | kept |
| `G.times.creation` | kept | kept | kept | kept |
| `G.times.lastmod` | kept | kept | kept | kept |
| `G.times.last-access` | kept | kept | kept | kept |
| `G.times.expiry` | kept | kept | kept | kept |
| `G.times.expires` | kept | kept | kept | kept |
| `G.times.usage` | kept | kept | kept | kept |
| `G.times.location` | kept | kept | kept | kept |
| `G.expanded` | kept | kept | kept | kept |
| `G.at-sequence` | kept | kept | kept | kept |
| `G.enable-auto-type` | kept | kept | kept | kept |
| `G.enable-searching` | kept | kept | kept | kept |
| `G.last-top-entry` | kept | kept | kept | kept |
| `G.previous-parent` | kept | kept | kept | dropped |
| `G.tags` | kept | kept | kept | dropped |
| `G.cd` | kept | kept | kept | kept |
| `G.cd.time` | dropped | dropped | dropped | dropped |
| `G.cd.only` | - | - | - | - |
| `E.icon` | kept | kept | kept | kept |
| `E.custom-icon` | kept | kept | kept | kept |
| `E.fg` | kept | kept | kept | kept |
| `E.bg` | kept | kept | kept | kept |
| `E.override-url` | kept | kept | kept | kept |
| `E.quality-check` | kept | kept | kept | dropped |
| `E.tags` | kept | kept | kept | kept |
| `E.previous-parent` | kept | kept | kept | dropped |
| `E.times.creation` | kept | kept | kept | kept |
| `E.times.lastmod` | kept | kept | kept | kept |
| `E.times.last-access` | kept | kept | kept | kept |
| `E.times.expiry` | kept | kept | kept | kept |
| `E.times.expires` | kept | kept | kept | kept |
| `E.times.usage` | kept | kept | kept | kept |
| `E.times.location` | kept | kept | kept | kept |
| `E.title` | kept | kept | kept | kept |
| `E.string` | kept | kept | kept | kept |
| `E.protected` | kept | kept | kept | kept |
| `E.protected.flag` | kept | kept | kept | kept |
| `E.binary` | kept | kept | kept | kept |
| `E.at.enabled` | kept | kept | kept | kept |
| `E.at.obfuscation` | kept | kept | kept | kept |
| `E.at.sequence` | kept | kept | kept | kept |
| `E.at.association` | kept | kept | kept | kept |
| `E.history` | kept | kept | kept | kept |
| `E.cd` | kept | kept | kept | kept |
| `E.cd.time` | dropped | dropped | dropped | dropped |
| `E.cd.only` | - | - | - | - |
| `D.shared` | kept | kept | kept | kept |
| `D.only` | - | - | - | - |
| `XC.meta` | dropped | dropped | dropped | dropped |
| `X.*` (18 positions) | dropped | dropped | dropped | dropped |
| `XA.*` (4 positions) | dropped | dropped | dropped | dropped |

A new master key, with `MasterKeyChanged` set to that moment as KeePass's window sets it (read),
changes nothing else. A change of key derivation keeps a key of the plugin's own in the KDF
parameters only because the probe copies it into the new parameters. KeePass's database settings
dialog, at every OK, builds the parameters anew from its own fields
(`DatabaseSettingsForm.GetKdfParameters`, read), so a key of the plugin's own there is lost the next
time anybody accepts that dialog, whatever was changed. Save As keeps every place a save keeps. The
step down to 4.0 drops exactly the 4.1 places and the previous parents, which KeePassLib writes only
in a 4.1 file.

### 4.2 Capacity

Each place that every save kept, and a few that most saves kept, took a value of 1,024, 65,536 and
1,048,576 letters and digits drawn from a fixed seed, so that compression leaves the file about as
large as the value, in a 4.1 base of its own; each program then opened and saved that file once, as
in section 2. A custom icon's data is a PNG whose text chunk holds the value. The seconds are the
program's whole run, start included, on a machine that other work kept busy, so they rank nothing
finer than a factor of several.

| Place | Characters | File bytes | KeePass 2.62 | KeePass 2.47 | KeePassXC 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 | kdbxweb | pykeepass |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `E.at-window` | 1024 | 3358 | kept (0.4 s) | kept (0.7 s) | kept (2.0 s) | kept (0.6 s) | kept (0.3 s) | kept (0.3 s) | kept (0.4 s) | kept (2.2 s) |
| `E.at-window` | 65536 | 52622 | kept (0.4 s) | kept (0.9 s) | kept (1.1 s) | kept (0.7 s) | kept (4.1 s) | kept (0.4 s) | kept (0.2 s) | kept (2.1 s) |
| `E.at-window` | 1048576 | 791886 | kept (0.6 s) | kept (2.0 s) | kept (1.1 s) | kept (2.3 s) | kept (0.6 s) | kept (0.6 s) | kept (0.5 s) | kept (4.6 s) |
| `E.cd` | 1024 | 3358 | kept (0.6 s) | kept (0.9 s) | kept (0.7 s) | kept (0.6 s) | kept (2.8 s) | kept (1.3 s) | kept (0.3 s) | kept (2.5 s) |
| `E.cd` | 65536 | 52590 | kept (0.5 s) | kept (1.0 s) | kept (1.1 s) | kept (3.5 s) | kept (0.5 s) | kept (0.4 s) | kept (0.3 s) | kept (2.5 s) |
| `E.cd` | 1048576 | 791870 | kept (0.5 s) | kept (1.2 s) | kept (1.6 s) | kept (0.9 s) | kept (0.9 s) | kept (1.2 s) | kept (0.3 s) | kept (3.3 s) |
| `E.override-url` | 1024 | 3358 | kept (0.4 s) | kept (1.3 s) | kept (1.5 s) | kept (0.6 s) | kept (0.5 s) | kept (0.6 s) | kept (0.3 s) | kept (4.7 s) |
| `E.override-url` | 65536 | 52542 | kept (0.6 s) | kept (0.8 s) | kept (4.8 s) | kept (0.7 s) | kept (0.6 s) | kept (0.7 s) | kept (0.3 s) | kept (2.3 s) |
| `E.override-url` | 1048576 | 791806 | kept (0.8 s) | kept (1.1 s) | kept (1.7 s) | kept (0.9 s) | kept (0.5 s) | kept (0.6 s) | kept (0.4 s) | kept (1.9 s) |
| `E.tags` | 1024 | 3358 | kept (0.7 s) | kept (1.1 s) | kept (0.5 s) | kept (0.4 s) | kept (0.4 s) | kept (0.3 s) | kept (0.4 s) | kept (7.2 s) |
| `E.tags` | 65536 | 52558 | kept (0.3 s) | kept (0.9 s) | kept (0.6 s) | kept (0.8 s) | kept (0.7 s) | kept (0.3 s) | kept (0.3 s) | kept (3.7 s) |
| `E.tags` | 1048576 | 791838 | kept (0.7 s) | kept (1.7 s) | kept (0.8 s) | kept (2.3 s) | kept (1.9 s) | kept (0.6 s) | kept (0.5 s) | kept (7.4 s) |
| `G.at-sequence` | 1024 | 3358 | kept (0.4 s) | kept (0.8 s) | kept (0.5 s) | kept (0.5 s) | kept (6.4 s) | kept (0.3 s) | kept (0.3 s) | kept (2.8 s) |
| `G.at-sequence` | 65536 | 52446 | kept (3.9 s) | kept (1.7 s) | kept (0.9 s) | kept (2.0 s) | kept (1.2 s) | kept (1.0 s) | kept (0.4 s) | kept (6.2 s) |
| `G.at-sequence` | 1048576 | 791694 | kept (0.6 s) | kept (1.0 s) | kept (1.1 s) | kept (0.9 s) | kept (1.0 s) | kept (0.5 s) | kept (0.6 s) | kept (5.4 s) |
| `G.cd` | 1024 | 3358 | kept (0.5 s) | kept (1.0 s) | kept (2.5 s) | kept (0.4 s) | kept (0.5 s) | kept (0.5 s) | kept (0.3 s) | kept (3.0 s) |
| `G.cd` | 65536 | 52494 | kept (0.4 s) | kept (0.7 s) | kept (3.9 s) | kept (0.6 s) | kept (0.5 s) | kept (0.6 s) | kept (0.4 s) | kept (2.4 s) |
| `G.cd` | 1048576 | 791774 | kept (0.4 s) | kept (0.8 s) | kept (1.3 s) | kept (1.5 s) | kept (1.2 s) | kept (0.6 s) | kept (0.3 s) | kept (3.2 s) |
| `G.notes` | 1024 | 3342 | kept (0.3 s) | kept (7.3 s) | kept (1.0 s) | kept (0.9 s) | kept (0.5 s) | kept (0.8 s) | kept (0.2 s) | kept (15.4 s) |
| `G.notes` | 65536 | 52366 | kept (0.4 s) | kept (9.0 s) | kept (1.0 s) | kept (0.7 s) | kept (1.3 s) | kept (0.6 s) | kept (0.5 s) | kept (7.7 s) |
| `G.notes` | 1048576 | 791646 | kept (1.0 s) | kept (1.3 s) | kept (1.0 s) | kept (1.7 s) | kept (1.4 s) | kept (0.6 s) | kept (2.6 s) | kept (4.3 s) |
| `H.pcd` | 1024 | 3488 | kept (3.4 s) | kept (2.8 s) | kept (1.6 s) | kept (0.8 s) | kept (1.6 s) | kept (1.7 s) | kept (3.5 s) | kept (33.1 s) |
| `H.pcd` | 65536 | 68000 | kept (0.6 s) | kept (1.5 s) | kept (0.7 s) | kept (2.6 s) | kept (1.3 s) | kept (0.5 s) | kept (3.1 s) | kept (10.3 s) |
| `H.pcd` | 1048576 | 1051040 | kept (1.0 s) | kept (1.8 s) | kept (0.7 s) | kept (1.9 s) | kept (1.7 s) | kept (1.6 s) | kept (0.4 s) | kept (2.5 s) |
| `M.cd` | 1024 | 3358 | kept (1.6 s) | kept (1.4 s) | kept (0.5 s) | kept (0.7 s) | kept (0.6 s) | kept (0.5 s) | kept (5.9 s) | kept (10.0 s) |
| `M.cd` | 65536 | 52270 | kept (0.5 s) | kept (1.2 s) | kept (2.2 s) | kept (0.8 s) | kept (0.4 s) | kept (1.1 s) | kept (0.4 s) | kept (2.0 s) |
| `M.cd` | 1048576 | 791534 | kept (0.8 s) | kept (1.9 s) | kept (2.8 s) | kept (1.8 s) | kept (3.0 s) | kept (0.7 s) | kept (0.7 s) | kept (3.5 s) |
| `M.desc` | 1024 | 3342 | kept (0.6 s) | kept (0.8 s) | kept (0.5 s) | kept (0.6 s) | kept (0.3 s) | kept (0.4 s) | kept (0.2 s) | kept (3.4 s) |
| `M.desc` | 65536 | 52126 | kept (0.5 s) | kept (0.8 s) | kept (0.7 s) | kept (5.1 s) | kept (0.6 s) | kept (0.5 s) | kept (0.5 s) | kept (4.4 s) |
| `M.desc` | 1048576 | 791390 | kept (0.8 s) | kept (1.3 s) | kept (1.2 s) | kept (0.9 s) | kept (0.7 s) | kept (0.8 s) | kept (0.5 s) | kept (2.2 s) |
| `M.icon.data` | 1024 | 3614 | kept (0.5 s) | kept (1.1 s) | rewritten (to <924 bytes sha256:90a484cd0eb2ead8>) (0.8 s) | rewritten (to <1161 bytes sha256:747629ec267f759c>... (0.7 s) | kept (0.6 s) | kept (0.3 s) | kept (0.4 s) | kept (5.6 s) |
| `M.icon.data` | 65536 | 65198 | kept (0.6 s) | kept (1.1 s) | rewritten (to <49411 bytes sha256:e5b8ede70d39f780... (1.0 s) | rewritten (to <49648 bytes sha256:18deada282750f7e... (0.7 s) | kept (5.8 s) | kept (1.0 s) | kept (0.3 s) | kept (2.6 s) |
| `M.icon.data` | 1048576 | 997438 | kept (0.7 s) | kept (12.5 s) | rewritten (to <788702 bytes sha256:aadabad21abb547... (2.3 s) | rewritten (to <788939 bytes sha256:fb3b7d422a2fcc1... (1.4 s) | kept (2.5 s) | kept (1.4 s) | kept (0.8 s) | kept (8.2 s) |
| `M.icon.name` | 1024 | 3358 | kept (1.1 s) | dropped (2.1 s) | dropped (5.0 s) | dropped (0.5 s) | kept (0.4 s) | kept (0.3 s) | kept (0.5 s) | kept (9.5 s) |
| `M.icon.name` | 65536 | 52366 | kept (0.8 s) | dropped (1.1 s) | dropped (0.8 s) | dropped (0.8 s) | kept (0.7 s) | kept (0.9 s) | kept (0.4 s) | kept (7.5 s) |
| `M.icon.name` | 1048576 | 791614 | kept (0.9 s) | dropped (5.8 s) | dropped (0.6 s) | dropped (0.5 s) | kept (2.2 s) | kept (1.0 s) | kept (0.5 s) | kept (2.5 s) |
| `M.name` | 1024 | 3358 | kept (0.4 s) | kept (1.4 s) | kept (3.4 s) | kept (0.5 s) | kept (0.3 s) | kept (0.4 s) | kept (0.3 s) | kept (3.7 s) |
| `M.name` | 65536 | 52110 | kept (0.4 s) | kept (0.9 s) | kept (0.7 s) | kept (1.2 s) | kept (0.6 s) | kept (0.7 s) | kept (0.4 s) | kept (2.3 s) |
| `M.name` | 1048576 | 791374 | kept (0.8 s) | kept (2.8 s) | kept (0.9 s) | kept (0.9 s) | kept (0.6 s) | kept (0.7 s) | kept (1.4 s) | kept (2.6 s) |
| `M.user` | 1024 | 3342 | kept (0.3 s) | kept (1.2 s) | kept (0.7 s) | kept (0.5 s) | kept (0.4 s) | kept (0.4 s) | kept (0.3 s) | kept (4.1 s) |
| `M.user` | 65536 | 52142 | kept (0.5 s) | kept (1.0 s) | kept (0.8 s) | kept (0.7 s) | kept (0.7 s) | kept (1.2 s) | kept (0.3 s) | kept (2.9 s) |
| `M.user` | 1048576 | 791390 | kept (0.5 s) | kept (1.4 s) | kept (2.5 s) | kept (1.3 s) | kept (1.0 s) | kept (0.8 s) | kept (0.5 s) | kept (7.8 s) |

No program refused a file or cut a value short at any of the three sizes. The longest save, 33.1
seconds in the recorded run and 57.4 in the second, was in both runs pykeepass on the 1 KiB public
custom data, longer than its own save of the 1 MiB value in the same place, so the seconds show the
busy machine more than the size. KeePassXC 2.4.2 and 2.6.6 decode a custom icon and encode it again,
so its data comes back as other bytes at every size; the value itself came through at all three
sizes, moved from the `tEXt` chunk into a compressed `zTXt` chunk (`recorded\icon-chunks.txt`, on
the second run's files); KeePass 2.47 and those two releases drop an icon's name at every size, as
at the small size of section 2. The format's own limit is the Int32 length of a header field and of
a variant dictionary entry (read), so about 2 GB; the XML places have no limit of their own in
KeePassLib (read).

### 4.3 Who sees each place (read)

Nothing here was run; no window was opened.

- **The outer header** is readable by anyone holding the file, without the password: the public
  custom data and a key in the KDF parameters are plain bytes before the encrypted part. KeePass
  shows neither in any window (no form reads `PublicCustomData`), and its KDBX 3.1 export empties
  the public custom data and every node's custom data while it writes (the 3.1 export in
  `KeePassKdbx2.cs`). KeePassXC 2.7.12 and 2.8.0 show three keys of their own, `KPXC_PUBLIC_NAME`,
  `KPXC_PUBLIC_COLOR` and `KPXC_PUBLIC_ICON`, on the unlock screen and no other key.
- **`Meta`.** KeePass's database settings show and edit the name, description, default user name,
  color, history limits, the recycle bin, the templates group and the master key reminders; Database
  Maintenance shows `MaintenanceHistoryDays` and lists the database's custom data under Plugin Data,
  with a button that deletes a key. KeePass's window writes `LastSelectedGroup`,
  `LastTopVisibleGroup` and the selected group's `LastTopVisibleEntry` from what it shows when it
  saves the active database and when the tab changes (`MainForm.SaveViewState`), so those three hold
  what the window last showed. KeePassXC shows the name, description, default user name, recycle bin
  and history limits, and none of `MaintenanceHistoryDays`, `Color`, the master key reminders or the
  memory protection.
- **Custom icons** are shown wherever a node uses them, and in KeePass's icon picker with their
  names; KeePass and KeePassXC 2.7 and later can delete unused icons on request, and KeePassXC's
  purge writes a deletion record for each.
- **A group or an entry.** Name, notes, tags, times, auto-type settings and the expiry are shown in
  both programs' dialogs; KeePass shows the node's custom data on a Plugin Data tab and KeePassXC
  under Properties. KeePassXC shows neither `OverrideURL` nor `DataTransferObfuscation`.
- **Exports.** KeePass's XML export writes the whole XML in plain text, every place of `Meta`, the
  tree and the unknown elements it kept, and nothing of either header. Its HTML export and print
  hold the entries' strings, times, tags and auto-type associations and the groups' notes, and no
  custom data. KeePassXC's XML export holds the same XML; its CSV holds ten columns of standard
  fields (`RESULTS.md` section 2.2).
- **The plugin's own routes** (`openapi.yaml` of the product) read a group's identifier, name,
  notes, parent and times and an entry's identifier, title, times, expiry, strings and attachments,
  and write a group's name and notes and an entry's strings, attachments and expiry. No route
  reaches the headers, `Meta`, a custom icon, custom data, tags or any other place of this
  inventory.

## 5. What the sources show beside the measurement (read)

Each item is read from the program's source at the release named, and nothing here was run.

**KeePass 2.62** (`KeePassLib`). Unknown outer and inner header fields are skipped while reading
(`KdbxFile.ReadHeaderField`, `ReadInnerHeaderField`), so nothing of them reaches the writer; the
comment field is among them. The writer builds the binary pool from the attachments of the tree
alone (`ProtectedBinarySet.AddFrom`), which is where an unreferenced binary goes. `KdfParameters`
keeps every key the dictionary held (`DeserializeExt` copies them), and `Randomize` changes `S`
alone. `PublicCustomData` is read whole and written whenever it holds a key. In a merge,
`MergeInDbProperties` returns at once under Keep existing; otherwise `SettingsChanged` decides
`Color` and which copy wins a public custom data key both hold, every key of the arriving copy that
the open copy lacks is added, the name, description and default user name follow their own times,
and the recycle bin and the templates group follow theirs and must name a group that exists; nothing
else in `Meta` is merged. `MergeInCustomIcons` runs under every method, Keep existing included, adds
the arriving copy's icons, replaces an icon by a newer `LastModificationTime`, and removes an icon a
deletion record names. `PwGroup.AssignProperties` copies every field of a group but `IsExpanded`. A
load resets `MemoryProtection` to its defaults (`CommonCleanUpRead`). An element the reader does not
know is skipped wherever it stands (`ReadUnknown`), and so is a `LastModificationTime` in a group's
or an entry's custom data item.

**KeePassXC** (2.4.2, 2.6.6, 2.7.12, 2.8.0). The comment and any field of an unknown identifier are
skipped with a warning (`Kdbx4Reader::readHeaderField`); fields 5, 6, 8, 9 and 10 in a 4.x file
refuse it. The KDF parameters are parsed into a KDF object and written from it, so a key of the
plugin's own there is lost, and `S` is fresh at every save (`Kdbx4Writer`, the call to `setKey` with
`updateTransformSalt`). Public custom data is kept whole and written back with its keys in
alphabetical order; `Merger::mergeMetadata` leaves it alone (a `TODO` there). 2.8.0 adds a key
`KPXC_PUBLIC_UUID` of its own for quick unlock (`Database::publicUuid`); 2.7.12 and 2.8.0 show
`KPXC_PUBLIC_NAME`, `KPXC_PUBLIC_COLOR` and `KPXC_PUBLIC_ICON` on the unlock screen. The inner
header's protection flag is dropped while reading and every attachment written protected; an unknown
inner field is skipped. `Generator` is kept as read. `MasterKeyChangeForceOnce` is never parsed.
Every non-empty custom data dictionary, of the database, a group, an entry or a history item,
receives `_LAST_MODIFIED` at load, with the load time. A merge copies a group's name, notes, icon,
expiry time and modification time from the newer group and nothing else; an entry goes whole;
deletion records are applied only in the Synchronize mode that KeeShare alone sets. 2.4.2 and 2.6.6
accept a 4.1 file and always write 4.0, which drops every 4.1 field, and decode every custom icon
and encode it again as PNG.

**kdbxweb 2.1.1.** An outer or inner header field of an unknown identifier makes `load` throw a
`TypeError`, since the field table has no entry for it. The comment is read and not written. The KDF
dictionary is kept whole with `S` fresh; public custom data is written back; a binary no entry
refers to is kept at a save and removed by the clean up after a merge. The XML is built again from
the object model, so every unknown element and attribute goes. `KdbxMeta.merge` takes the name,
description and default user name by their own times, keeps the open copy's memory protection and
last selected groups, and takes `MaintenanceHistoryDays`, `Color`, `MasterKeyChangeRec`,
`MasterKeyChangeForce` and the history limits from the arriving copy unless the open copy changed
them in the same session. A merge never copies a group's or an entry's custom data, tags, quality
check or previous parent into an existing node.

**pykeepass 4.2.0.** The header is parsed with `construct`, whose mapping knows the identifiers 0,
1, 2, 3, 4, 7, 11 and 12 and no other, so field 13, and fields 5, 6, 8, 9 and 10 in a 4.x file,
refuse the file; so does an inner field other than 0 to 3. The comment and public custom data are
kept as bytes. `rotate_seeds` makes the master seed, the IV, the inner key and `S` fresh at every
save. The XML is an `lxml` tree written back as it was read, so unknown elements, attributes and
comments are kept; the XML declaration and the whitespace between elements are not.

## 6. What the measurement of the phone programs needs

Nothing here ran a phone program. The family leaves what a later run of Keepass2Android and
KeePassDX needs without reading its code (`RUN.md`, "For the phone programs"): the command that
makes 20 files, `android\make-bases.py`; the list of places with the dump line that shows each,
`work\android\places.txt`, recorded as `recorded\android-places.txt`; and `android\check.py`, which
prints one line per place for a file a phone program saved (kept, dropped, rewritten and to what) or
for a merge result (which copy's value each place holds). The files use Argon2d with 16 MiB, since
KeePassDX refuses less than 8 MiB (read), and every place KeePassLib cannot write is in a file of
its own, since KeePassDX and KeePassDroid refuse the comment, an unknown header field with content
and an unknown element in `Meta` (read, section 5) and would otherwise refuse every place at once.

What the sources leave open, and the run has to settle:

- **Each candidate of section 7** through a save and both merge directions in each program:
  Keepass2Android's merge at a save conflict and its synchronization of a remote file, and
  KeePassDX's Merge from. The source of both programs keeps every candidate at a save (read);
  Keepass2Android's merge is KeePassLib's Synchronize, with one change read in its source: a
  database custom data key it copies loses its time.
- **Custom icons.** Whether an icon no node uses survives a Keepass2Android and a KeePassDX save and
  merge, and whether either program ever writes a deletion record for an icon.
- **The header.** Whether KeePassDX really refuses `place-comment.kdbx` and `place-hfield.kdbx`, as
  its source reads, and what both programs make of the public custom data and of a key of the
  plugin's own in the KDF parameters; KeePassDX copies the arriving copy's KDF parameters in a merge
  when its `SettingsChanged` is later (read).
- **Unknown XML.** Whether KeePassDX refuses `place-xml-meta.kdbx`, `place-xml-other.kdbx` and
  `place-xml-empty.kdbx`, which its source suggests for the first two and not the third.
- **`Meta` fields KeePassDX merges by its own rules** (read): the master key reminders with
  `MasterKeyChanged`, the history limits and `Color` with `SettingsChanged`, and the recycle bin and
  templates group with their times but without checking that the group exists.
- **What a phone program writes into places it does not edit**: `Generator` (empty in
  Keepass2Android, `KeePassDX` in KeePassDX, read), `HeaderHash`, which KeePassDX writes into a 4.x
  file (read), and the format version, which both can bring down to 3.1 (read for Keepass2Android,
  measured for an AES-KDF file in `RESULTS.md` section 2.3).

## 7. Places worth considering

The places sorted by the four behaviors, each with what it could carry for the store, which programs
keep it and which do not, how much it holds, who can see it, and what the phone programs still have
to show. Every statement about a desktop program is from sections 2 to 4.

### 7.1 Kept by every save and united by every merge

**No place of the inventory is united by every merge measured.** The nearest:

- **Custom icons** (`M.icon.*`). An icon only one copy holds was united by every KeePass method,
  Keep existing included, and by every KeePassXC release; every save kept an icon, used or not.
  kdbxweb's merge deleted every icon no node uses and wrote a deletion record for each, and KeePass
  would carry those records on (section 3). KeePassXC 2.4.2 and 2.6.6 decode and encode the PNG
  again, and KeePass 2.47 and those two releases drop the name and the time. Could carry: an
  add-only set, an icon per element, its UUID the element's identifier and its data an image; the
  name, where 4.1 survives, as text. Holds: 1 MiB in an icon's data and in its name came through
  every save that keeps them (section 4.2), the data as a PNG whose text chunk KeePassXC 2.4.2 and
  2.6.6 move into a compressed chunk, and the data has to decode as an image, since those two
  releases write an icon they cannot decode as empty (read). Seen by: anybody choosing an icon in
  KeePass or KeePassXC, and a maintenance command in both deletes unused icons. The phones: whether
  an unused icon survives their saves and merges.
- **The public custom data** (`H.pcd.only`) was united by Synchronize, Overwrite if newer, Overwrite
  existing and Create new IDs, and never merged by Keep existing, KeePassXC or kdbxweb: there the
  open copy's set stays. Every save kept it.
- **A database custom data key** (`M.cd.only`) was united by every KeePass method but Keep existing,
  and by kdbxweb; KeePassXC kept the open copy's set when its two loads fell in the same second and
  took the arriving copy's set whole when they did not.
- **A deletion record** (`D.only`) was united by Synchronize and kdbxweb only, as `RESULTS.md` has
  it.

### 7.2 Kept by every save and never touched by a merge

A place whose open copy's value stays in every merge holds a memory of that copy that a stale copy
merged in does not overwrite.

- **`LastSelectedGroup` and `LastTopVisibleGroup`** (`M.last-selected`, `M.last-top-visible`): kept
  by every save of every program, and the open copy's value stayed in every merge of every program,
  kdbxweb included. Could carry: two group UUIDs, 32 bytes, a per-copy memory. They are not free:
  KeePass's window writes both from what it shows whenever it saves the active database (read), and
  KeePassXC writes the zero UUID for a group that does not exist (read), so a value has to name a
  group of the file and lasts until the next save from KeePass's window. Seen by: nobody as a value;
  KeePass selects and scrolls to those groups when it opens the file. The phones: whether either
  program keeps them through a save and a merge.
- **`MaintenanceHistoryDays`, `MasterKeyChangeRec`, `MasterKeyChangeForce`, `HistoryMaxItems` and
  `HistoryMaxSize`**: kept by every save; the open copy's value stayed under every KeePass method
  and every KeePassXC release; kdbxweb took the arriving copy's. Could carry: two 32-bit and three
  64-bit integers, a per-copy memory. Each has a meaning the programs act on: the reminder and
  forcing of a master key change, the history kept per entry, and the age Database Maintenance
  offers, so a value that is not the operator's setting changes what KeePass does. Seen by: the
  operator in KeePass's database settings and maintenance dialogs; KeePassXC shows the history
  limits. The phones: KeePassDX merges the reminders by `MasterKeyChanged` and the history limits by
  `SettingsChanged` (read).
- **A group's `IsExpanded`** (`G.expanded`): kept by every save; the open copy's value stayed under
  KeePass and KeePassXC, kdbxweb took the newer group's. One bit per group, written by KeePass's and
  KeePassXC's trees whenever a group is opened or closed there (read).
- **The public custom data** (`H.pcd`), under KeePassXC and kdbxweb only (section 7.1 and 7.3 for
  KeePass).

### 7.3 Merged by a rule that is useful in itself: the later time wins, per field

- **The database name, description and default user name** (`M.name`, `M.desc`, `M.user`), each with
  its own time: kept by every save; under Synchronize, Overwrite if newer, Create new IDs and
  kdbxweb the later time wins field by field, with the open copy's value at equal times; KeePassXC
  never merges them, so the open copy's stays. Could carry: three texts, each with a time of its
  own. Seen by: the operator, in the database settings of KeePass and KeePassXC; none is reached by
  the plugin's routes. Holds: section 4.2. The phones: Keepass2Android merges them as KeePass does
  and KeePassDX by the newer time (read); to be measured.
- **A database custom data key with its time** (`M.cd`, `M.cd.time`): the later time wins under
  KeePass's three methods and kdbxweb; KeePassXC 2.4.2 and 2.6.6 drop the time and none of the
  releases merges a key by its time (`RESULTS.md` sections 2.1 and 2.2 for the rest).
- **A custom icon's name, time and data** (`M.icon.name`, `M.icon.time`, `M.icon.data`): the later
  icon time wins under every KeePass method, Keep existing included, and under kdbxweb; KeePassXC
  2.7.12 and 2.8.0 keep the open copy's, 2.4.2 and 2.6.6 drop name and time. Could carry: per icon a
  text and a time that merge by the time, on top of the set of section 7.1.
- **The recycle bin and the templates group** (`M.bin-*`, `M.template*`): the later of their own
  times wins under KeePass's three methods and kdbxweb, the open copy's under KeePassXC; each must
  name an existing group, or KeePass falls back to the other copy's or to none (read).
- **The public custom data** under KeePass: per key, the copy whose `SettingsChanged` is later wins,
  with the open copy's at equal times, and keys only one copy holds are added, so the rule is a time
  for the whole dictionary and not per key.
- **`Color`**, by `SettingsChanged`, under KeePass's three methods; kdbxweb took the arriving
  copy's.

### 7.4 Kept by KeePass only: a hint when present, harmless when gone

- **A key of the plugin's own in the KDF parameters** (`H.kdf`): kept by KeePass 2.62 and 2.47,
  kdbxweb and pykeepass at every save and through a new master key, Save As and the step down to
  4.0; dropped by every KeePassXC save; never merged, so the open copy's stays. Lost at the next OK
  of KeePass's database settings dialog (read). Readable by anyone holding the file, without the
  password. Could carry: a hint that the copy has not passed through KeePassXC since the plugin last
  wrote it.
- **The public custom data** (`H.pcd`) is kept by every program and merged only by KeePass, so as a
  hint it marks nothing; it belongs in 7.1 to 7.3. Readable without the password, shown by no
  KeePass window, and emptied by KeePass's KDBX 3.1 export (read).
- **`MasterKeyChangeForceOnce`**: kept by KeePass and pykeepass, dropped by every KeePassXC release
  and kdbxweb; a Boolean whose meaning is to force a master key change, so not a carrier.
- **What a single desktop program keeps, or none**, and so carries nothing across them: the comment,
  an unknown element, attribute or XML comment (pykeepass alone), a binary no entry refers to and a
  time on a node's custom data item (kdbxweb and pykeepass), the end of header field's extra bytes
  (pykeepass), and the bytes after the last block (no program). An outer or inner header field of an
  unknown identifier makes kdbxweb and pykeepass refuse the whole file, and the comment and an
  unknown outer field make KeePassDX and KeePassDroid refuse it (read), so writing any of them
  breaks the file for a program rather than losing the place alone.
