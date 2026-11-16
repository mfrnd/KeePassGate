#!/bin/sh
# The repeats of item 2, item 4 (write-protected), item 3 (the overwrite) and item 1's saves, one
# after another. Summary lines go to out/batch-item2b.txt and out/batch-<file>.txt.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-item2b.txt"
K=com.kunzisoft.keepass.libre
r() { sh "$P/run-B.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
o() { sh "$P/run-ow.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
r kpdx kpdx-B-from.flow $K PHONE DESK mfPD "4"
r kpdx kpdx-ext-merge.flow $K PHONE DESK extmPD "3"
r kpdx kpdx-ext-reload.flow $K PHONE DESK extrPD "3"
r kpdx kpdx-ext-save.flow $K DESK PHONE extsDP "2"
o kpdx kpdx-ro.flow $K B41 ro-r1
o kpdx kpdx-ro.flow $K B41 ro-r2
echo "### batch item2b done $(ts)" >> "$S"
sh "$P/batch-ow.sh" "$P/batches/item3.txt"
sh "$P/batch-ow.sh" "$P/batches/item1-saves.txt"
echo "### batch big done $(ts)" >> "$S"
