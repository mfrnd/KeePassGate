#!/bin/sh
# Item 3's last repeats, then item 1's chains, one after another. Summary lines in
# out/batch-chains.txt.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-chains.txt"
K=com.kunzisoft.keepass.libre
o() { sh "$P/run-ow.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
o kpdx kpdx-ow-fg.flow $K OLD ow-fg-diff-plus11-r3 ARR=NEW-diff MODE=plus11 TAG=ow-fg-diff-plus11-r3
o kpdx kpdx-ow-fg.flow $K OLD ow-fg-diff-plus15-r2 ARR=NEW-diff MODE=plus15 TAG=ow-fg-diff-plus15-r2
o kpdx kpdx-ch-entry2.flow $K B41 ch-entry2
o kpdx kpdx-ch-records.flow $K B41 ch-records
o kpdx kpdx-ch-group.flow $K B41 ch-group
o kpdx kpdx-ch-delete.flow $K B41 ch-delete
o kpdx kpdx-ch-move.flow $K B41 ch-move
o kpdx kpdx-ch-copy.flow $K B41 ch-copy
echo "### batch chains done $(ts)" >> "$S"
