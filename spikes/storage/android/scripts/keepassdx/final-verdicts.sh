#!/bin/sh
# Windows side, Git Bash: read back the last batch's runs and write their verdicts to
# out/final-verdicts.txt: the overwrite verdicts with the dialogs counted, the write-protected runs'
# file checks and what each screen offered, and each chain step's changed item lines.
M=<work>
X="$M/probe/bin/Release/net48/StoreProbe.exe"
O="$M/out/final-verdicts.txt"
W="$M/work/runs/kpdx"; L="$M/out/runs/kpdx"
{
  echo "### batch summaries"; cut -c1-200 "$M/out/batch-after.txt"
  for r in ow-lock-diff-plus60-r1 ow-noauto-save-plus60-r2; do
    [ -d "$W/$r" ] || continue
    echo "### $r dialogs $(grep -c 'dialog message' "$L/$r.log")"
    grep -o 'devstat [a-z-]*: [0-9]* bytes, mtime [^s]*sha256 [0-9a-f]\{16\}' "$L/$r.log"
    "$X" owverdict "$(cygpath -w "$W/$r/kpdx-$r.kdbx")" 2>/dev/null
  done
  for r in ro-r1 ro-r2; do
    [ -f "$L/$r.log" ] || continue
    echo "### $r"
    grep -E 'flow:[0-9]+ log |waitchange:|FAILED|^device ' "$L/$r.log" | cut -c1-200
    grep -oE "(text|desc)='[^']+'" "$L/$r.log" | sort | uniq -c | sort -rn | head -60
  done
  for c in ch-entry3 ch-move ch-copy2 ch-group2; do
    [ -d "$W/$c" ] || continue
    echo "### $c"
    grep -E 'flow:[0-9]+ log |FAILED|waitchange:|dialog message' "$L/$c.log" | cut -c1-160
    LINES=45 sh "$M/scripts/chain.sh" B41 "$W/$c" 2>&1 | grep -v ' H20[0-9-]*T[0-9:]*Z \|path \|t.accessed\|usage\|R meta Gen' | cut -c1-170
  done
} > "$O" 2>&1
wc -l "$O"
