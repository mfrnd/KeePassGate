#!/bin/sh
# Windows side, Git Bash: item 8, the gaps of the first measurement. Files Keepass2Android 1.15-r6
# and KeePassDX 4.5.8 saved on 2026-11-15 (measured-android.md, steps 1, 4, 5 and 6), copied from
# that measurement's working directory with the bases they were given, each synchronized back into
# KeePass (KeePassLib 2.62) in both directions with the base KeePass would hold, by sync.sh.
M=<work>
S=<measure-work>/work
X="$M/probe/bin/Release/net48/StoreProbe.exe"
I="$M/work/item8"; mkdir -p "$I/base" "$I/files"
for b in S-argon S-aes S-aes-nocd S-aes-plain; do cp "$S/base/$b.kdbx" "$I/base/"; done
cp "$S/runs/k2a/rename-g/k2a-rename-g.kdbx" "$S/runs/k2a/delete/k2a-delete.kdbx" "$S/runs/k2a/move/k2a-move.kdbx" \
   "$S/runs/k2a/save-aes/k2a-save-aes.kdbx" "$S/runs/k2a/save-aes-nocd/k2a-save-aes-nocd.kdbx" "$S/runs/k2a/save-aes-plain/k2a-save-aes-plain.kdbx" \
   "$S/runs/kpdx/move/kpdx-move.kdbx" "$S/runs/kpdx/save-aes/kpdx-save-aes.kdbx" "$S/runs/kpdx/save-aes-nocd/kpdx-save-aes-nocd.kdbx" \
   "$S/runs/kpdx/save-aes-plain/kpdx-save-aes-plain.kdbx" "$I/files/"
sha256sum "$I"/base/*.kdbx "$I"/files/*.kdbx > "$I/inputs.sha256"
"$X" dumpdir "$(cygpath -w "$I")" >/dev/null 2>&1
for f in k2a-rename-g:S-argon k2a-delete:S-argon k2a-move:S-argon k2a-save-aes:S-aes k2a-save-aes-nocd:S-aes-nocd k2a-save-aes-plain:S-aes-plain \
         kpdx-move:S-argon kpdx-save-aes:S-aes kpdx-save-aes-nocd:S-aes-nocd kpdx-save-aes-plain:S-aes-plain; do
  n=${f%%:*}; b=${f##*:}
  sh "$M/scripts/sync.sh" "item8-$n" "$I/files/$n.kdbx" "$I/base/$b.kdbx" "$I/base/$b.kdbx.dump.txt"
done
