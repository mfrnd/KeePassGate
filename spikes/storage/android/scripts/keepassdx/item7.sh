#!/bin/sh
# Windows side, Git Bash: item 7, the way back. Every result file KeePassDX left that differs from
# the file it was given, opened and saved by KeePassLib 2.62 and synchronized with the untouched
# desktop copy in both directions (sync.sh). The desktop copy is the file the desktop holds: the
# base for an operation, the desktop's copy for a merge (DESK, or the arriving hazard copy), the
# desktop's later save for an overwrite. Prints one summary line per synchronize.
#   sh item7.sh [<run dir glob> ...]     default: every run under work/runs/kpdx and work/B/kpdx
M=<work>
B="$M/work/base"
desk_of() {   # <run name> -> desktop copy name
  case "$1" in
    mfPD*|mfDP*|extmPD*|extmDP*|extrPD*|extrDP*|extsPD*|extsDP*) echo DESK ;;
    ow-*-diff-*|ow-noauto-*|ow-lock-*|ow-kill-*) echo NEW-diff ;;
    ow-*-same-*) echo NEW-same ;;
    ow-*-ident-*) echo NEW-ident ;;
    save-HATT*) echo HATT ;;
    mfHATTrev*) echo HATTo ;;
    mfHATT*) echo HATTa ;;
    mfHHIST*) echo HHISTa ;;
    mfHNESTrev*) echo HNESTg ;;
    mfHNEST*) echo HNESTa ;;
    ch-history*|mfHL*) echo HLa ;;
    save-*) echo "${1#save-}" ;;
    *) echo B41 ;;
  esac
}
given_of() {  # <run name> -> the file KeePassDX was given (for the diff against the base)
  case "$1" in
    mfPD*|extmPD*|extrPD*|extsPD*) echo PHONE ;;
    mfDP*|extmDP*|extrDP*|extsDP*) echo DESK ;;
    ow-*) echo OLD ;;
    save-*) echo "${1#save-}" ;;
    mfHATTrev*) echo HATTa ;;
    mfHATT*) echo HATTo ;;
    mfHHIST*) echo B41 ;;
    mfHNESTg*) echo HNESTg ;;
    mfHNESTe*) echo HNESTe ;;
    mfHNESTrev*) echo HNESTa ;;
    ch-history*|mfHL*) echo HL ;;
    *) echo B41 ;;
  esac
}
dirs="$*"; [ -n "$dirs" ] || dirs="$(ls -d "$M"/work/runs/kpdx/* "$M"/work/B/kpdx/* 2>/dev/null)"
for R in $dirs; do
  run=$(basename "$R"); run=${run%-r[0-9]}
  for f in "$R"/*.kdbx; do
    [ -f "$f" ] || continue
    case "$f" in *-src.kdbx) continue ;; esac
    g=$(given_of "$run"); d=$(desk_of "$run")
    [ "$(sha256sum < "$f" | cut -d' ' -f1)" = "$(sha256sum < "$B/$g.kdbx" | cut -d' ' -f1)" ] && continue
    sh "$M/scripts/sync.sh" "back-$(basename "$R")-$(basename "$f" .kdbx)" "$f" "$B/$d.kdbx" "$B/$g.kdbx.dump.txt" | tr '\n' ' '
    echo
  done
done
