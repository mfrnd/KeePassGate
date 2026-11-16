#!/bin/sh
# Items 5 and 6: the hazards read from source, and history limits. A summary line per run goes to
# out/batch-item56.txt.
. "$(dirname "$0")/env.sh"
S="$D/out/batch-item56.txt"
K=com.kunzisoft.keepass.libre
b() { sh "$P/run-B.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
o() { sh "$P/run-ow.sh" "$@" </dev/null >> "$S" 2>&1; tail -1 "$S"; }
# 5.1 two attachments whose first four MD5 bytes agree: a plain save; one arriving by merge
o kpdx kpdx-save.flow $K HATT save-HATT-r1
o kpdx kpdx-save.flow $K HATT save-HATT-r2
b kpdx kpdx-B-from.flow $K HATTo HATTa mfHATT "1 2"
b kpdx kpdx-B-from.flow $K HATTa HATTo mfHATTrev "1"
# 5.2 attachments only in arriving history
b kpdx kpdx-B-from.flow $K B41 HHISTa mfHHIST "1 2"
# 5.3 nested groups deleted on one side, something inside changed later on the other
b kpdx kpdx-B-from-hang.flow $K HNESTg HNESTa mfHNESTg "1 2"
b kpdx kpdx-B-from-hang.flow $K HNESTe HNESTa mfHNESTe "1 2"
b kpdx kpdx-B-from.flow $K HNESTa HNESTg mfHNESTrev "1"
# 6 history limits, by edits and by a merge
o kpdx kpdx-ch-history.flow $K HL ch-history-r1
b kpdx kpdx-B-from.flow $K HL HLa mfHL "1 2"
echo "### batch item56 done $(ts)" >> "$S"
