#!/bin/sh
# Item 2: both merge paths, both directions, every answer. PD is the phone's copy open and the
# desktop's arriving; DP the reverse. A summary line per run goes to out/batch-item2.txt.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-item2.txt"
r() { sh "$P/run-B.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
r kpdx kpdx-B-from.flow com.kunzisoft.keepass.libre PHONE DESK mfPD "2 3"
r kpdx kpdx-B-from.flow com.kunzisoft.keepass.libre DESK PHONE mfDP "1 2 3"
r kpdx kpdx-ext-merge.flow com.kunzisoft.keepass.libre PHONE DESK extmPD "1 2"
r kpdx kpdx-ext-merge.flow com.kunzisoft.keepass.libre DESK PHONE extmDP "1 2"
r kpdx kpdx-ext-reload.flow com.kunzisoft.keepass.libre PHONE DESK extrPD "1 2"
r kpdx kpdx-ext-reload.flow com.kunzisoft.keepass.libre DESK PHONE extrDP "1"
r kpdx kpdx-ext-save.flow com.kunzisoft.keepass.libre PHONE DESK extsPD "1 2"
r kpdx kpdx-ext-save.flow com.kunzisoft.keepass.libre DESK PHONE extsDP "1"
echo "### batch item2 done $(ts)" >> "$S"
