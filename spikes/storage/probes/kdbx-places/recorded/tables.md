# kdbx-places: the tables

Written by `scripts/summarize.py` from `out/`.

## Saves: what an open and save, or an unrelated edit, does to each place

Cell for the 4.1 Argon2d base; a set that differs is named after it (e41 AES-KDF 4.1, a40 Argon2d 4.0, e40 AES-KDF 4.0).

| place | keepass-2.62-resave | keepass-2.62-edit | keepass-2.47-resave | keepass-2.47-edit | keepassxc-2.4.2-add | keepassxc-2.6.6-add | keepassxc-2.7.12-add | keepassxc-2.8.0-add | kdbxweb-resave | kdbxweb-edit | pykeepass-resave | pykeepass-edit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `H.version` format version | kept | kept | rewritten (to 4.0) [a40: kept; e40: kept] | rewritten (to 4.0) [a40: kept; e40: kept] | rewritten (to 4.0) [a40: kept; e40: kept] | rewritten (to 4.0) [a40: kept; e40: kept] | kept | kept | kept | kept | kept | kept |
| `H.comment` outer header field 1, comment | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `H.kdf` a key of the plugin's own in the KDF parameters | kept | kept | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept |
| `H.kdf.only` a KDF key only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `H.pcd` public custom data, a string key both copies hold | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.uint64` public custom data, a UInt64 key | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.bytes` public custom data, a byte array key | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `H.pcd.only` public custom data, a key only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `I.unreferenced` an inner header binary no entry refers to | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept | kept | kept |
| `I.binary` an attachment's binary and its flags | kept | kept | kept | kept | rewritten (to kpg:E.binary:base flags=1 refs=1) | rewritten (to kpg:E.binary:base flags=1 refs=1) | rewritten (to kpg:E.binary:base flags=1 refs=1) | rewritten (to kpg:E.binary:base flags=1 refs=1) | kept | kept | kept | kept |
| `T.trailing` bytes after the last block | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `M.generator` Generator | rewritten (to ) | rewritten (to ) | rewritten (to ) | rewritten (to ) | kept | kept | kept | kept | rewritten (to KdbxWeb) | rewritten (to KdbxWeb) | kept | kept |
| `M.settings-changed` SettingsChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.name` DatabaseName | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.name-changed` DatabaseNameChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.desc` DatabaseDescription | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.desc-changed` DatabaseDescriptionChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.user` DefaultUserName | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.user-changed` DefaultUserNameChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-days` MaintenanceHistoryDays | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.color` Color | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-changed` MasterKeyChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-rec` MasterKeyChangeRec | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-force` MasterKeyChangeForce | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.key-force-once` MasterKeyChangeForceOnce | kept | kept | kept | kept | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `M.memprot` MemoryProtection, the five flags | kept; rewritten (to False) | kept; rewritten (to False) | kept; rewritten (to False) | kept; rewritten (to False) | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.icon.data` a custom icon's data | kept | kept | kept | kept | rewritten (to <107 bytes sha256:16e5493c45bc3b75>) | rewritten (to <344 bytes sha256:5d405497ccb5f0d0>) | kept | kept | kept | kept | kept | kept |
| `M.icon.name` a custom icon's name (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `M.icon.time` a custom icon's time (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `M.icon.unused` a custom icon no node uses | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.icon.only` a custom icon only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `M.bin-enabled` RecycleBinEnabled | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.bin-uuid` RecycleBinUUID | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.bin-changed` RecycleBinChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.template` EntryTemplatesGroup | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.template-changed` EntryTemplatesGroupChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-max-items` HistoryMaxItems | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.history-max-size` HistoryMaxSize | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.last-selected` LastSelectedGroup | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.last-top-visible` LastTopVisibleGroup | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.cd` the database's custom data, a key both copies hold | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `M.cd.time` its per-key time (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `M.cd.only` a key only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `G.name` group Name | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.notes` group Notes | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.icon` group IconID | kept | kept | kept | kept | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | kept | kept | kept | kept |
| `G.custom-icon` group CustomIconUUID | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.creation` group CreationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.lastmod` group LastModificationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.last-access` group LastAccessTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.expiry` group ExpiryTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.expires` group Expires | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.usage` group UsageCount | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.location` group LocationChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.expanded` group IsExpanded | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.at-sequence` group DefaultAutoTypeSequence | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.enable-auto-type` group EnableAutoType | kept | kept | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to true) | kept | kept | kept | kept |
| `G.enable-searching` group EnableSearching | kept | kept | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to false) | kept | kept | kept | kept |
| `G.last-top-entry` group LastTopVisibleEntry | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.previous-parent` group PreviousParentGroup (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `G.tags` group Tags (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | rewritten (to kpg, G.tags, base) | rewritten (to kpg, G.tags, base) | kept | kept |
| `G.cd` group custom data, a key both copies hold | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.cd.time` a time on a group custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept [a40: dropped; e40: dropped] | kept [a40: dropped; e40: dropped] | kept | kept |
| `G.cd.only` group custom data, a key only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `E.icon` entry IconID | kept | kept | kept | kept | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | kept | kept | kept | kept |
| `E.custom-icon` entry CustomIconUUID | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.fg` entry ForegroundColor | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.bg` entry BackgroundColor | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.override-url` entry OverrideURL | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.quality-check` entry QualityCheck (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `E.tags` entry Tags | kept | kept | rewritten (to kpg;E.tags;base) | rewritten (to kpg;E.tags;base) | kept | kept | kept | kept | rewritten (to kpg, E.tags, base) | rewritten (to kpg, E.tags, base) | kept | kept |
| `E.previous-parent` entry PreviousParentGroup (4.1) | kept | kept | dropped | dropped | dropped | dropped | kept | kept | kept | kept | kept | kept |
| `E.times.creation` entry CreationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.lastmod` entry LastModificationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.last-access` entry LastAccessTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.expiry` entry ExpiryTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.expires` entry Expires | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.usage` entry UsageCount | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.location` entry LocationChanged | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.title` entry Title | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.string` entry custom string | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.protected` entry protected custom string | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.protected.flag` its Protected attribute | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.binary` entry attachment reference | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.enabled` entry AutoType Enabled | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.obfuscation` entry AutoType DataTransferObfuscation | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.sequence` entry AutoType DefaultSequence | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.at.association` entry AutoType Association | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.history` entry History item | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.cd` entry custom data, a key both copies hold | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.cd.time` a time on an entry custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept [a40: dropped; e40: dropped] | kept [a40: dropped; e40: dropped] | kept | kept |
| `E.cd.only` entry custom data, a key only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `D.shared` a deletion record both copies hold, its time | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `D.only` a deletion record only one copy holds | - | - | - | - | - | - | - | - | - | - | - | - |
| `X.file` an unknown element in KeePassFile | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.meta` an unknown element in Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.memprot` in MemoryProtection | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.icon` in a custom icon | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.meta.cd-item` in a Meta custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.root` in Root | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.group` in a group | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.group.times` in a group's Times | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.group.cd-item` in a group custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry` in an entry | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.times` in an entry's Times | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.string` in an entry String | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.binary` in an entry Binary | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.autotype` in AutoType | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.association` in an Association | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.cd-item` in an entry custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.entry.history` in a history item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `X.deleted` in a DeletedObject | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `XA.meta` an unknown attribute on Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `XA.group` on a Group | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `XA.entry` on an Entry | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `XA.value` on a String's Value | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |
| `XC.meta` an XML comment in Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | kept | kept |

## Saves of the files holding a place no reader may expect

| file | place | keepass-2.62-resave | keepass-2.62-edit | keepass-2.47-resave | keepass-2.47-edit | keepassxc-2.4.2-add | keepassxc-2.6.6-add | keepassxc-2.7.12-add | keepassxc-2.8.0-add | kdbxweb-resave | kdbxweb-edit | pykeepass-resave | pykeepass-edit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| a41 risky-hfield | `H.field13` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | refused: TypeError | refused: TypeError | refused: MappingError | refused: MappingError |
| a41 risky-ifield | `I.field4` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | refused: TypeError | refused: TypeError | refused: MappingError | refused: MappingError |
| a41 risky-eoh | `H.eoh` | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 00d0ad0a) | rewritten (to 00d0ad0a) | kept | kept |
| e41 risky-hfield | `H.field13` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | refused: TypeError | refused: TypeError | refused: MappingError | refused: MappingError |
| e41 risky-ifield | `I.field4` | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | refused: TypeError | refused: TypeError | refused: MappingError | refused: MappingError |
| e41 risky-eoh | `H.eoh` | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 0d0a0d0a) | rewritten (to 00d0ad0a) | rewritten (to 00d0ad0a) | kept | kept |

## Merges: which copy's value each place holds afterwards

A is the copy whose times are older (or equal, in the second pair); each pair is merged
both ways. Sets: a41 (Argon2d 4.1) unless named; KeePassXC also with the slow key
derivation (s41); a set that differs is named in brackets.

| place | keepass-2.62-Synchronize | keepass-2.62-OverwriteIfNewer | keepass-2.62-OverwriteExisting | keepass-2.62-KeepExisting | keepass-2.62-CreateNewUuids | keepassxc-2.4.2 | keepassxc-2.6.6 | keepassxc-2.7.12 | keepassxc-2.8.0 | kdbxweb |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `H.version` format version | kept | kept | kept | kept | kept | rewritten (to 4.0) | rewritten (to 4.0) | kept | kept | kept |
| `H.comment` outer header field 1, comment | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `H.kdf` a key of the plugin's own in the KDF parameters | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | dropped | dropped | the open copy's stays; at equal times the open copy's |
| `H.kdf.only` a KDF key only one copy holds | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | dropped | dropped | the open copy's stays; at equal times the open copy's |
| `H.pcd` public custom data, a string key both copies hold | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `H.pcd.uint64` public custom data, a UInt64 key | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `H.pcd.bytes` public custom data, a byte array key | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `H.pcd.only` public custom data, a key only one copy holds | united | united | united | the open copy's stays; at equal times the open copy's | united | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `I.unreferenced` an inner header binary no entry refers to | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `I.binary` an attachment's binary and its flags | A older and open: arriving+new; B newer and open: new+open; at equal times the arriving co... | A older and open: arriving+new; B newer and open: new+open; at equal times the arriving co... | A older and open: arriving+new; B newer and open: arriving+new; at equal times arriving+ne... | A older and open: rewritten (to kpg:E.binary:A flags=0 refs=2); B newer and open: new+open... | A older and open: new+open; B newer and open: new+open; at equal times new+open / new+open | A older and open: new+rewritten (to kpg:E.binary:B flags=1 refs=1); B newer and open: new+... | A older and open: new+rewritten (to kpg:E.binary:B flags=1 refs=1); B newer and open: new+... | A older and open: new+rewritten (to kpg:E.binary:B flags=1 refs=1); B newer and open: new+... | A older and open: new+rewritten (to kpg:E.binary:B flags=1 refs=1); B newer and open: new+... | A older and open: new+open; B newer and open: new+open; at equal times the open copy's |
| `T.trailing` bytes after the last block | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `M.generator` Generator | rewritten (to ) | rewritten (to ) | rewritten (to ) | rewritten (to ) | rewritten (to ) | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | rewritten (to KdbxWeb) |
| `M.settings-changed` SettingsChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.name` DatabaseName | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.name-changed` DatabaseNameChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.desc` DatabaseDescription | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.desc-changed` DatabaseDescriptionChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.user` DefaultUserName | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.user-changed` DefaultUserNameChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.history-days` MaintenanceHistoryDays | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.color` Color | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.key-changed` MasterKeyChanged | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.key-rec` MasterKeyChangeRec | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.key-force` MasterKeyChangeForce | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.key-force-once` MasterKeyChangeForceOnce | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | dropped | dropped | dropped |
| `M.memprot` MemoryProtection, the five flags | A older and open: arriving+open; B newer and open: arriving+open; at equal times arriving+... | A older and open: arriving+open; B newer and open: arriving+open; at equal times arriving+... | A older and open: arriving+open; B newer and open: arriving+open; at equal times arriving+... | A older and open: arriving+open; B newer and open: arriving+open; at equal times arriving+... | A older and open: arriving+open; B newer and open: arriving+open; at equal times arriving+... | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `M.icon.data` a custom icon's data | the later time wins; at equal times the open copy's [a40: the open copy's stays; at equal times the open copy's; e40: the open copy's stays; at equal times the open copy's] | the later time wins; at equal times the open copy's [a40: the open copy's stays; at equal times the open copy's; e40: the open copy's stays; at equal times the open copy's] | the later time wins; at equal times the open copy's [a40: the open copy's stays; at equal times the open copy's; e40: the open copy's stays; at equal times the open copy's] | the later time wins; at equal times the open copy's [a40: the open copy's stays; at equal times the open copy's; e40: the open copy's stays; at equal times the open copy's] | the later time wins; at equal times the open copy's [a40: the open copy's stays; at equal times the open copy's; e40: the open copy's stays; at equal times the open copy's] | A older and open: rewritten (to <107 bytes sha256:2a817c7486b1f312>); B newer and open: re... | A older and open: rewritten (to <344 bytes sha256:6b256d9b7fb1071d>); B newer and open: re... | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.icon.name` a custom icon's name (4.1) | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | dropped | dropped | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.icon.time` a custom icon's time (4.1) | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | dropped | dropped | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.icon.unused` a custom icon no node uses | kept | kept | kept | kept | kept | kept | kept | kept | kept | dropped |
| `M.icon.only` a custom icon only one copy holds | united | united | united | united | united | united | united | united | united | dropped |
| `M.bin-enabled` RecycleBinEnabled | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.bin-uuid` RecycleBinUUID | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.bin-changed` RecycleBinChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.template` EntryTemplatesGroup | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `M.template-changed` EntryTemplatesGroupChanged | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `M.history-max-items` HistoryMaxItems | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.history-max-size` HistoryMaxSize | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's |
| `M.last-selected` LastSelectedGroup | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `M.last-top-visible` LastTopVisibleGroup | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `M.cd` the database's custom data, a key both copies hold | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the later time wins; at equal times the open copy's |
| `M.cd.time` its per-key time (4.1) | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | dropped | dropped | the open copy's stays; at equal times kept [s41: A older and open: rewritten (to 2026-11-16T13:01:58Z); B newer an...] | the open copy's stays; at equal times kept [s41: A older and open: rewritten (to 2026-11-16T13:07:28Z); B newer an...] | the later time wins; at equal times kept |
| `M.cd.only` a key only one copy holds | united | united | united | the open copy's stays; at equal times the open copy's | united | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | the open copy's stays; at equal times the open copy's [s41: the arriving copy's replaces; at equal times the arriving copy's] | united |
| `G.name` group Name | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.notes` group Notes | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.icon` group IconID | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | the later time wins; at equal times the open copy's |
| `G.custom-icon` group CustomIconUUID | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.creation` group CreationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.lastmod` group LastModificationTime | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept |
| `G.times.last-access` group LastAccessTime | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | A older and open: rewritten (to 2026-11-16T12:52:28Z); B newer and open: open; at equal ti... [s41: A older and open: rewritten (to 2026-11-16T12:53:10Z); B newer an...] | A older and open: rewritten (to 2026-11-16T12:52:33Z); B newer and open: open; at equal ti... [s41: A older and open: rewritten (to 2026-11-16T12:57:32Z); B newer an...] | A older and open: rewritten (to 2026-11-16T12:52:36Z); B newer and open: open; at equal ti... [s41: A older and open: rewritten (to 2026-11-16T13:01:56Z); B newer an...] | A older and open: rewritten (to 2026-11-16T12:52:37Z); B newer and open: open; at equal ti... [s41: A older and open: rewritten (to 2026-11-16T13:07:28Z); B newer an...] | the later time wins; at equal times kept |
| `G.times.expiry` group ExpiryTime | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.times.expires` group Expires | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `G.times.usage` group UsageCount | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.times.location` group LocationChanged | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept |
| `G.expanded` group IsExpanded | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.at-sequence` group DefaultAutoTypeSequence | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `G.enable-auto-type` group EnableAutoType | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | A older and open: rewritten (to false); B newer and open: rewritten (to null); at equal ti... | A older and open: rewritten (to false); B newer and open: rewritten (to null); at equal ti... | A older and open: rewritten (to false); B newer and open: rewritten (to null); at equal ti... | A older and open: rewritten (to false); B newer and open: rewritten (to null); at equal ti... | A older and open: rewritten (to null); B newer and open: rewritten (to null); at equal tim... |
| `G.enable-searching` group EnableSearching | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | A older and open: rewritten (to true); B newer and open: rewritten (to null); at equal tim... | A older and open: rewritten (to true); B newer and open: rewritten (to null); at equal tim... | A older and open: rewritten (to true); B newer and open: rewritten (to null); at equal tim... | A older and open: rewritten (to true); B newer and open: rewritten (to null); at equal tim... | A older and open: rewritten (to null); B newer and open: rewritten (to null); at equal tim... |
| `G.last-top-entry` group LastTopVisibleEntry | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | A older and open: rewritten (to 00000000-0000-0000-0000-000000000000); B newer and open: r... | A older and open: rewritten (to 00000000-0000-0000-0000-000000000000); B newer and open: r... | A older and open: rewritten (to 00000000-0000-0000-0000-000000000000); B newer and open: r... | A older and open: rewritten (to 00000000-0000-0000-0000-000000000000); B newer and open: r... | the later time wins; at equal times the open copy's |
| `G.previous-parent` group PreviousParentGroup (4.1) | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `G.tags` group Tags (4.1) | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | A older and open: rewritten (to kpg, G.tags, A); B newer and open: rewritten (to kpg, G.ta... |
| `G.cd` group custom data, a key both copies hold | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `G.cd.time` a time on a group custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | - |
| `G.cd.only` group custom data, a key only one copy holds | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `E.icon` entry IconID | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | rewritten (to 0) | the later time wins; at equal times the open copy's |
| `E.custom-icon` entry CustomIconUUID | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.fg` entry ForegroundColor | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.bg` entry BackgroundColor | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.override-url` entry OverrideURL | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.quality-check` entry QualityCheck (4.1) | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `E.tags` entry Tags | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | A older and open: rewritten (to kpg, E.tags, B); B newer and open: rewritten (to kpg, E.ta... |
| `E.previous-parent` entry PreviousParentGroup (4.1) | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | dropped | dropped | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `E.times.creation` entry CreationTime | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.lastmod` entry LastModificationTime | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept |
| `E.times.last-access` entry LastAccessTime | the later time wins; at equal times kept | the later time wins; at equal times kept | the arriving copy's replaces; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept |
| `E.times.expiry` entry ExpiryTime | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.times.expires` entry Expires | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.times.usage` entry UsageCount | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.times.location` entry LocationChanged | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept | the later time wins; at equal times kept |
| `E.title` entry Title | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.string` entry custom string | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.protected` entry protected custom string | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.protected.flag` its Protected attribute | kept | kept | kept | kept | kept | kept | kept | kept | kept | kept |
| `E.binary` entry attachment reference | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | A older and open: rewritten (to ref 1 = kpg:E.binary:B); B newer and open: open; at equal ... |
| `E.at.enabled` entry AutoType Enabled | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.at.obfuscation` entry AutoType DataTransferObfuscation | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.at.sequence` entry AutoType DefaultSequence | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.at.association` entry AutoType Association | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's |
| `E.history` entry History item | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times new / new | A older and open: new; B newer and open: new; at equal times kept | kept | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times kept | A older and open: new; B newer and open: new; at equal times kept |
| `E.cd` entry custom data, a key both copies hold | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `E.cd.time` a time on an entry custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | - |
| `E.cd.only` entry custom data, a key only one copy holds | the later time wins; at equal times the arriving copy's | the later time wins; at equal times the arriving copy's | the arriving copy's replaces; at equal times the arriving copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the later time wins; at equal times the open copy's | the open copy's stays; at equal times the open copy's |
| `D.shared` a deletion record both copies hold, its time | the later time wins; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept | the open copy's stays; at equal times kept |
| `D.only` a deletion record only one copy holds | united | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | the open copy's stays; at equal times the open copy's | united |
| `X.file` an unknown element in KeePassFile | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.meta` an unknown element in Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.memprot` in MemoryProtection | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.icon` in a custom icon | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.meta.cd-item` in a Meta custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.root` in Root | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.group` in a group | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.group.times` in a group's Times | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.group.cd-item` in a group custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry` in an entry | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.times` in an entry's Times | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.string` in an entry String | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.binary` in an entry Binary | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.autotype` in AutoType | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.association` in an Association | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.cd-item` in an entry custom data item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.entry.history` in a history item | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `X.deleted` in a DeletedObject | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XA.meta` an unknown attribute on Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XA.group` on a Group | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XA.entry` on an Entry | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XA.value` on a String's Value | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |
| `XC.meta` an XML comment in Meta | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped | dropped |

## KeePass 2.62: a master key change, a KDF change, Save As, and 4.1 brought down to 4.0

| place | rekey | kdf | save-as | down40 |
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
| `M.generator` | rewritten (to ) | rewritten (to ) | rewritten (to ) | rewritten (to ) |
| `M.settings-changed` | kept | kept | kept | kept |
| `M.name` | kept | kept | kept | kept |
| `M.name-changed` | kept | kept | kept | kept |
| `M.desc` | kept | kept | kept | kept |
| `M.desc-changed` | kept | kept | kept | kept |
| `M.user` | kept | kept | kept | kept |
| `M.user-changed` | kept | kept | kept | kept |
| `M.history-days` | kept | kept | kept | kept |
| `M.color` | kept | kept | kept | kept |
| `M.key-changed` | rewritten (to 2026-11-16T13:10:03Z) [e41: rewritten (to 2026-11-16T13:10:05Z)] | kept | kept | kept |
| `M.key-rec` | kept | kept | kept | kept |
| `M.key-force` | kept | kept | kept | kept |
| `M.key-force-once` | kept | kept | kept | kept |
| `M.memprot` | kept; rewritten (to False) | kept; rewritten (to False) | kept; rewritten (to False) | kept; rewritten (to False) |
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
| `X.file` | dropped | dropped | dropped | dropped |
| `X.meta` | dropped | dropped | dropped | dropped |
| `X.memprot` | dropped | dropped | dropped | dropped |
| `X.icon` | dropped | dropped | dropped | dropped |
| `X.meta.cd-item` | dropped | dropped | dropped | dropped |
| `X.root` | dropped | dropped | dropped | dropped |
| `X.group` | dropped | dropped | dropped | dropped |
| `X.group.times` | dropped | dropped | dropped | dropped |
| `X.group.cd-item` | dropped | dropped | dropped | dropped |
| `X.entry` | dropped | dropped | dropped | dropped |
| `X.entry.times` | dropped | dropped | dropped | dropped |
| `X.entry.string` | dropped | dropped | dropped | dropped |
| `X.entry.binary` | dropped | dropped | dropped | dropped |
| `X.entry.autotype` | dropped | dropped | dropped | dropped |
| `X.entry.association` | dropped | dropped | dropped | dropped |
| `X.entry.cd-item` | dropped | dropped | dropped | dropped |
| `X.entry.history` | dropped | dropped | dropped | dropped |
| `X.deleted` | dropped | dropped | dropped | dropped |
| `XA.meta` | dropped | dropped | dropped | dropped |
| `XA.group` | dropped | dropped | dropped | dropped |
| `XA.entry` | dropped | dropped | dropped | dropped |
| `XA.value` | dropped | dropped | dropped | dropped |
| `XC.meta` | dropped | dropped | dropped | dropped |

## Capacity: a value of 1 KiB, 64 KiB and 1 MiB characters in one place

| place | chars | file bytes | keepass-2.62-resave | keepass-2.47-resave | keepassxc-2.4.2-add | keepassxc-2.6.6-add | keepassxc-2.7.12-add | keepassxc-2.8.0-add | kdbxweb-resave | pykeepass-resave |
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
