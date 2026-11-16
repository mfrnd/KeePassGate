# The places of the format, by program

| Place | What it is | Base | k2a save | k2a unrelated | k2a entry edit | k2a merge A+B, B+A, tie A+B, B+A | kpdx save | kpdx unrelated | kpdx entry edit | kpdx merge A+B, B+A, tie A+B, B+A | KeePassLib merge A+B, B+A, tie A+B, B+A |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H.version | format version | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| H.comment | outer header field 1, comment | place-comment | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| H.kdf | a key of the plugin's own in the KDF parameters | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; arriving; arriving; arriving | open; open; open; open |
| H.kdf.only | a KDF key only one copy holds | the pairs | - | - | - | open; open; open; open | - | - | - | arriving; arriving; arriving; arriving | open; open; open; open |
| H.pcd | public custom data, a string key both copies hold | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | open; open; open; open | arriving; open; open; open |
| H.pcd.uint64 | public custom data, a UInt64 key | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | open; open; open; open | arriving; open; open; open |
| H.pcd.bytes | public custom data, a byte array key | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | open; open; open; open | arriving; open; open; open |
| H.pcd.only | public custom data, a key only one copy holds | the pairs | - | - | - | union; union; union; union | - | - | - | open; open; open; open | union; union; union; union |
| H.eoh | end of header field's content | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| H.field13 | an outer header field of an undefined identifier | place-hfield | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| I.unreferenced | an inner header binary no entry refers to | place-unreferenced | dropped | dropped | dropped | -; -; -; - | kept | kept | kept | -; -; -; - | -; -; -; - |
| I.binary | an attachment's binary and its flags | clean-base | kept | kept | rewritten (to kpg:E.binary:base flags=0 refs=2) | arriving+new; new+open; arriving; arriving | kept | kept | rewritten (to kpg:E.binary:base flags=0 refs=2) | new+open; new+open; new+open; new+open | arriving+new; new+open; arriving; arriving |
| I.field4 | an inner header field of an undefined identifier | place-ifield | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| T.trailing | bytes after the last block | place-trailing | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| M.generator | Generator | clean-base | kept | kept | kept | same; same; same; same | rewritten (to KeePassDX) | rewritten (to KeePassDX) | rewritten (to KeePassDX) | rewritten (to KeePassDX); rewritten (to KeePassDX); rewritten (to KeePassDX); rewritten (to KeePassDX) | same; same; same; same |
| M.settings-changed | SettingsChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.name | DatabaseName | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.name-changed | DatabaseNameChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.desc | DatabaseDescription | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.desc-changed | DatabaseDescriptionChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.user | DefaultUserName | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.user-changed | DefaultUserNameChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.history-days | MaintenanceHistoryDays | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | open; open; open; open | open; open; open; open |
| M.color | Color | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.key-changed | MasterKeyChanged | clean-base | kept | kept | kept | open; open; same; same | kept | kept | kept | arriving; open; same; same | open; open; same; same |
| M.key-rec | MasterKeyChangeRec | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; open; open; open | open; open; open; open |
| M.key-force | MasterKeyChangeForce | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; open; open; open | open; open; open; open |
| M.key-force-once | MasterKeyChangeForceOnce | clean-base | kept | kept | kept | open; open; open; open | dropped | dropped | dropped | arriving; open; arriving; open | open; open; open; open |
| M.memprot | MemoryProtection, the five flags | clean-base | kept; rewritten (to False) | kept; rewritten (to False) | kept; rewritten (to False) | arriving+open; arriving+open; arriving+open; arriving+open | kept | kept | kept | open; open; open; open | arriving+open; arriving+open; arriving+open; arriving+open |
| M.icon.data | a custom icon's data | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | open; open; open; open | arriving; open; open; open |
| M.icon.name | a custom icon's name (4.1) | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.icon.time | a custom icon's time (4.1) | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.icon.unused | a custom icon no node uses | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| M.icon.only | a custom icon only one copy holds | the pairs | - | - | - | union; union; union; union | - | - | - | union; union; union; union | union; union; union; union |
| M.bin-enabled | RecycleBinEnabled | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.bin-uuid | RecycleBinUUID | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.bin-changed | RecycleBinChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.template | EntryTemplatesGroup | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.template-changed | EntryTemplatesGroupChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.history-max-items | HistoryMaxItems | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; open; open; open | open; open; open; open |
| M.history-max-size | HistoryMaxSize | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; open; open; open | open; open; open; open |
| M.last-selected | LastSelectedGroup | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | open; open; open; open | open; open; open; open |
| M.last-top-visible | LastTopVisibleGroup | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | open; open; open; open | open; open; open; open |
| M.cd | the database's custom data, a key both copies hold | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| M.cd.time | its per-key time (4.1) | clean-base | dropped | dropped | dropped | dropped; dropped; dropped; dropped | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| M.cd.only | a key only one copy holds | the pairs | - | - | - | union; union; union; union | - | - | - | union; union; union; union | union; union; union; union |
| G.name | group Name | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.notes | group Notes | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.icon | group IconID | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.custom-icon | group CustomIconUUID | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| G.times.creation | group CreationTime | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| G.times.lastmod | group LastModificationTime | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | rewritten (to 2026-11-16T15:01:22Z) | arriving; open; same; same | arriving; open; same; same |
| G.times.last-access | group LastAccessTime | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | rewritten (to 2026-11-16T15:01:22Z) | arriving; open; same; same | arriving; open; same; same |
| G.times.expiry | group ExpiryTime | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.times.expires | group Expires | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| G.times.usage | group UsageCount | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.times.location | group LocationChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| G.expanded | group IsExpanded | clean-base | kept | kept | kept | open; open; open; open | kept | kept | kept | arriving; open; open; open | open; open; open; open |
| G.at-sequence | group DefaultAutoTypeSequence | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.enable-auto-type | group EnableAutoType | clean-base | rewritten (to true) | rewritten (to true) | rewritten (to true) | rewritten (to null); rewritten (to null); rewritten (to null); rewritten (to false) | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.enable-searching | group EnableSearching | clean-base | rewritten (to false) | rewritten (to false) | rewritten (to false) | rewritten (to null); rewritten (to null); rewritten (to null); rewritten (to true) | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.last-top-entry | group LastTopVisibleEntry | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.previous-parent | group PreviousParentGroup (4.1) | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| G.tags | group Tags (4.1) | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| G.cd | group custom data, a key both copies hold | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; arriving; arriving; arriving | arriving; open; arriving; arriving |
| G.cd.time | a time on a group custom data item | place-node-cd-time | dropped | dropped | dropped | -; -; -; - | kept | kept | kept | -; -; -; - | -; -; -; - |
| G.cd.only | group custom data, a key only one copy holds | the pairs | - | - | - | arriving; open; arriving; arriving | - | - | - | arriving; union; union; union | arriving; open; arriving; arriving |
| E.icon | entry IconID | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.custom-icon | entry CustomIconUUID | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| E.fg | entry ForegroundColor | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.bg | entry BackgroundColor | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.override-url | entry OverrideURL | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.quality-check | entry QualityCheck (4.1) | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | dropped | open; open; open; open | arriving; open; arriving; arriving |
| E.tags | entry Tags | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.previous-parent | entry PreviousParentGroup (4.1) | clean-base | kept | kept | kept | arriving; open; open; open | kept | kept | kept | arriving; open; open; open | arriving; open; open; open |
| E.times.creation | entry CreationTime | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| E.times.lastmod | entry LastModificationTime | clean-base | kept | kept | rewritten (to 2026-11-16T15:00:13Z) | arriving; open; same; same | kept | kept | rewritten (to 2026-11-16T15:01:22Z) | arriving; open; same; same | arriving; open; same; same |
| E.times.last-access | entry LastAccessTime | clean-base | kept | kept | rewritten (to 2026-11-16T15:00:13Z) | arriving; open; same; same | kept | kept | rewritten (to 2026-11-16T15:01:22Z) | arriving; open; same; same | arriving; open; same; same |
| E.times.expiry | entry ExpiryTime | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.times.expires | entry Expires | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| E.times.usage | entry UsageCount | clean-base | kept | kept | rewritten (to 72) | arriving; open; arriving; arriving | kept | kept | rewritten (to 72) | arriving; open; open; open | arriving; open; arriving; arriving |
| E.times.location | entry LocationChanged | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | arriving; open; same; same | arriving; open; same; same |
| E.title | entry Title | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.string | entry custom string | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.protected | entry protected custom string | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.protected.flag | its Protected attribute | clean-base | kept | kept | kept | same; same; same; same | kept | kept | kept | same; same; same; same | same; same; same; same |
| E.binary | entry attachment reference | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | rewritten (to ref 1 = kpg:E.binary:B); open; open; open | arriving; open; arriving; arriving |
| E.at.enabled | entry AutoType Enabled | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.at.obfuscation | entry AutoType DataTransferObfuscation | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.at.sequence | entry AutoType DefaultSequence | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.at.association | entry AutoType Association | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; open; open; open | arriving; open; arriving; arriving |
| E.history | entry History item | clean-base | kept | kept | kept; added (kpg:E.title:base) | new; new; same; same | kept | kept | kept; added (kpg:E.title:base) | new; new; same; same | new; new; same; same |
| E.cd | entry custom data, a key both copies hold | clean-base | kept | kept | kept | arriving; open; arriving; arriving | kept | kept | kept | arriving; arriving; arriving; arriving | arriving; open; arriving; arriving |
| E.cd.time | a time on an entry custom data item | place-node-cd-time | dropped | dropped | dropped | -; -; -; - | kept | kept | kept | -; -; -; - | -; -; -; - |
| E.cd.only | entry custom data, a key only one copy holds | the pairs | - | - | - | arriving; open; arriving; arriving | - | - | - | arriving; union; union; union | arriving; open; arriving; arriving |
| D.shared | a deletion record both copies hold, its time | clean-base | kept | kept | kept | arriving; open; same; same | kept | kept | kept | open; open; same; same | arriving; open; same; same |
| D.only | a deletion record only one copy holds | the pairs | - | - | - | union; union; union; union | - | - | - | open; open; open; open | union; union; union; union |
| X.file | an unknown element in KeePassFile | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.meta | an unknown element in Meta | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.memprot | in MemoryProtection | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.icon | in a custom icon | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.meta.cd-item | in a Meta custom data item | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.root | in Root | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.group | in a group | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.group.times | in a group's Times | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.group.cd-item | in a group custom data item | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry | in an entry | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.times | in an entry's Times | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.string | in an entry String | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.binary | in an entry Binary | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.autotype | in AutoType | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.association | in an Association | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.cd-item | in an entry custom data item | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.entry.history | in a history item | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| X.deleted | in a DeletedObject | place-xml-empty | dropped | dropped | dropped | -; -; -; - | not opened | not opened | not opened | -; -; -; - | -; -; -; - |
| XA.meta | an unknown attribute on Meta | place-attrs | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| XA.group | on a Group | place-attrs | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| XA.entry | on an Entry | place-attrs | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| XA.value | on a String's Value | place-attrs | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
| XC.meta | an XML comment in Meta | place-xml-comment | dropped | dropped | dropped | -; -; -; - | dropped | dropped | dropped | -; -; -; - | -; -; -; - |
