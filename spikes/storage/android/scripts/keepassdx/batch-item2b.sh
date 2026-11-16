#!/bin/sh
# Item 2, the repeats: a third counted "Merge from" PD run (r3 pulled before the save, not counted),
# a second external-change Merge data PD run (r1 failed at unlock), and a Reload data PD run with the
# save after it that shows what the program then holds.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-item2b.txt"
r() { sh "$P/run-B.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
r kpdx kpdx-B-from.flow com.kunzisoft.keepass.libre PHONE DESK mfPD "4"
r kpdx kpdx-ext-merge.flow com.kunzisoft.keepass.libre PHONE DESK extmPD "3"
r kpdx kpdx-ext-reload.flow com.kunzisoft.keepass.libre PHONE DESK extrPD "3"
echo "### batch item2b done $(ts)" >> "$S"
