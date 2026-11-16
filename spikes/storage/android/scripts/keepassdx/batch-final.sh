#!/bin/sh
# The last item 3 run: autosave off, Save data after the dialog.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-after.txt"
K=com.kunzisoft.keepass.libre
sh "$P/run-ow.sh" kpdx kpdx-ow-noauto.flow $K OLD ow-noauto-save-plus60-r2 ARR=NEW-diff MODE=plus60 TAG=x ANSWER=Save.data </dev/null >> "$S" 2>&1
echo "### batch final done $(ts)" >> "$S"
