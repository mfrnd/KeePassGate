#!/bin/sh
# What remains on the emulator after batch-all1: the merge that hung, watched (item 2 and 5.3); a
# third stale copy merged into a KeePassDX merge result on the device (item 5.5); Keepass2Android
# with KeePassDX's files (item 7); the operation chains (item 1). Summary lines in out/batch-rest.txt.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-rest.txt"
K=com.kunzisoft.keepass.libre
A=keepass2android.keepass2android
b() { sh "$P/run-B.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
o() { sh "$P/run-ow.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
B="$D/work/base"
cp "$D/work/B/kpdx/mfPD-r1/kpdx-mfPD.kdbx" "$B/DXMFPD.kdbx"
cp "$D/work/runs/kpdx/save-B41/kpdx-save-B41.kdbx" "$B/DXSAVE.kdbx"
cp "$D/work/runs/kpdx/save-B41aes/kpdx-save-B41aes.kdbx" "$B/DXAES.kdbx"
cp "$D/work/runs/kpdx/ch-entry/s04-notes.kdbx" "$B/DXEDIT.kdbx"
b kpdx kpdx-B-from-hang.flow $K PHONE DESK mfPDhang "1 2"
b kpdx kpdx-B-from.flow $K DXMFPD B41 mfStale "1"
o k2a k2a-A.flow $A DXMFPD k2a-DXMFPD ENTRY=k2a-added
o k2a k2a-A.flow $A DXSAVE k2a-DXSAVE ENTRY=k2a-added
o k2a k2a-A.flow $A DXAES k2a-DXAES ENTRY=k2a-added
o k2a k2a-A.flow $A DXEDIT k2a-DXEDIT ENTRY=k2a-added
o k2a k2a-merge.flow $A DXEDIT k2a-merge-DXEDIT-DESK ENTRY=k2a-merged ARRIVING=DESK
echo "### batch rest done $(ts)" >> "$S"
