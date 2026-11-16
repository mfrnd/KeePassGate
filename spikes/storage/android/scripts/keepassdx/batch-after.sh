#!/bin/sh
# After the chains: item 3's and item 4's last repeats, then the continuations of item 1's chains.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-after.txt"
K=com.kunzisoft.keepass.libre
o() { sh "$P/run-ow.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
o kpdx kpdx-ow-noauto.flow $K OLD ow-noauto-save-plus60-r1 ARR=NEW-diff MODE=plus60 TAG=x ANSWER=Save.data
o kpdx kpdx-ow-noauto.flow $K OLD ow-noauto-merge-plus60-r1 ARR=NEW-diff MODE=plus60 TAG=x ANSWER=Merge.data
o kpdx kpdx-ow-lock.flow $K OLD ow-lock-diff-plus5-r1 ARR=NEW-diff MODE=plus5 TAG=x
o kpdx kpdx-ow-lock.flow $K OLD ow-lock-diff-plus60-r1 ARR=NEW-diff MODE=plus60 TAG=x
o kpdx kpdx-ro.flow $K B41 ro-r1
o kpdx kpdx-ro.flow $K B41 ro-r2
o kpdx kpdx-ch-entry3.flow $K B41 ch-entry3
o kpdx kpdx-ch-move.flow $K B41 ch-move
o kpdx kpdx-ch-copy2.flow $K B41 ch-copy2
o kpdx kpdx-ch-group2.flow $K B41 ch-group2
echo "### batch after done $(ts)" >> "$S"
